using System.Runtime.InteropServices;
using System.Windows;

namespace SnapActions.Core;

internal static class ClipboardTransaction
{
    private const uint CF_BITMAP = 2;
    private const uint CF_METAFILEPICT = 3;
    private const uint CF_PALETTE = 9;
    private const uint CF_UNICODETEXT = 13;
    private const uint CF_ENHMETAFILE = 14;
    private const uint CF_OWNERDISPLAY = 0x0080;
    private const uint CF_DSPBITMAP = 0x0082;
    private const uint CF_DSPMETAFILEPICT = 0x0083;
    private const uint CF_DSPENHMETAFILE = 0x008E;
    private const uint CF_PRIVATEFIRST = 0x0200;
    private const uint CF_PRIVATELAST = 0x02FF;
    private const uint CF_GDIOBJFIRST = 0x0300;
    private const uint CF_GDIOBJLAST = 0x03FF;
    private const uint GMEM_MOVEABLE = 0x0002;
    private static IntPtr _clipboardOwnerWindow;
    // must still duplicate every one of them before clipboard-mutating capture is allowed.
    private static readonly HashSet<string> RoundTrippableFormats = new(StringComparer.Ordinal)
    {
        System.Windows.DataFormats.UnicodeText, System.Windows.DataFormats.Text,
        System.Windows.DataFormats.Rtf, System.Windows.DataFormats.Html,
        System.Windows.DataFormats.CommaSeparatedValue, System.Windows.DataFormats.FileDrop,
        System.Windows.DataFormats.Bitmap,
    };

    internal readonly record struct ClipboardFormatRead(
        string Format, bool ReadSucceeded, bool HasValue);

    internal readonly record struct ClipboardObservation(
        uint Sequence, IntPtr OwnerWindow, uint OwnerProcessId);

    internal enum ClipboardMutationOwnership
    {
        None,
        // Attributable single-step write; exact post-read observation may authorize restoration.
        Owned,
        // Target-owned multi-step write; readable, but never authoritative enough to restore over.
        OwnedUnrestorable,
        Ambiguous,
    }

    internal enum NativeClipboardHandleKind
    {
        GlobalMemory,
        GdiObject,
    }

    private readonly record struct NativeClipboardWriteResult(
        bool Success, bool NeedsRollback, ClipboardObservation Observation);

    internal sealed class NativeClipboardFormatBackup(
        uint format,
        IntPtr handle,
        NativeClipboardHandleKind handleKind)
    {
        internal uint Format { get; } = format;
        internal IntPtr Handle { get; set; } = handle;
        internal NativeClipboardHandleKind HandleKind { get; } = handleKind;
    }

    internal sealed class ClipboardSnapshot : IDisposable
    {
        private List<NativeClipboardFormatBackup>? _nativeBackups;

        internal ClipboardSnapshot(
            Dictionary<string, object> data,
            ClipboardObservation observation)
        {
            Data = data;
            Observation = observation;
        }

        internal ClipboardSnapshot(
            Dictionary<string, object> data,
            ClipboardObservation observation,
            List<NativeClipboardFormatBackup> nativeBackups)
            : this(data, observation)
        {
            _nativeBackups = nativeBackups;
        }

        internal Dictionary<string, object> Data { get; }
        internal ClipboardObservation Observation { get; }
        internal bool HasNativeRestorePayload =>
            Volatile.Read(ref _nativeBackups) != null;

        internal List<NativeClipboardFormatBackup>? TakeNativeBackups() =>
            Interlocked.Exchange(ref _nativeBackups, null);

        public void Dispose()
        {
            ReleaseNativeBackups();
            GC.SuppressFinalize(this);
        }

        ~ClipboardSnapshot() => ReleaseNativeBackups();

        private void ReleaseNativeBackups()
        {
            var backups = Interlocked.Exchange(ref _nativeBackups, null);
            if (backups != null)
                FreeNativeClipboardBackups(backups);
        }
    }

    internal sealed record ClipboardNativeApi(
        Func<IntPtr> GetOwnerWindow,
        Func<IntPtr, bool> Open,
        Func<ClipboardObservation> Observe,
        Func<List<NativeClipboardFormatBackup>?> DuplicateFormats,
        Func<bool> Empty,
        Func<List<NativeClipboardFormatBackup>, bool> RestoreFormats,
        Func<bool> Close);

    private static readonly ClipboardNativeApi NativeClipboard = new(
        GetValidClipboardOwnerWindow,
        OpenClipboard,
        ObserveClipboard,
        DuplicateClipboardFormats,
        EmptyClipboard,
        backups => RestoreNativeClipboardBackups(backups),
        CloseClipboard);

    private sealed class NativeClipboardWritePreparation(
        IntPtr ownerWindow,
        IntPtr textHandle,
        List<NativeClipboardFormatBackup> backups)
    {
        internal IntPtr OwnerWindow { get; } = ownerWindow;
        internal IntPtr TextHandle { get; set; } = textHandle;
        internal List<NativeClipboardFormatBackup> Backups { get; } = backups;
    }

    internal static void SetClipboardOwnerWindow(IntPtr hwnd) =>
        Interlocked.Exchange(ref _clipboardOwnerWindow, hwnd);

    internal static bool IsCompleteSnapshot(
        ClipboardObservation before,
        ClipboardObservation after,
        IEnumerable<ClipboardFormatRead> reads) =>
        before.Sequence != 0
        && before == after
        && reads.All(read =>
            !RoundTrippableFormats.Contains(read.Format)
            || (read.ReadSucceeded && read.HasValue));

    internal static ClipboardMutationOwnership ClassifyClipboardMutation(
        ClipboardObservation before,
        ClipboardObservation after,
        bool requestDelivered,
        uint expectedOwnerProcessId,
        bool targetStillValid)
    {
        bool expectedOwner = after.OwnerWindow != IntPtr.Zero
                             && after.OwnerProcessId != 0
                             && after.OwnerProcessId == expectedOwnerProcessId;
        if (after.Sequence == before.Sequence)
        {
            // Delayed rendering can transfer clipboard ownership before Windows increments the
            // sequence. An expected new owner is sufficient to read and trigger rendering.
            return requestDelivered
                   && targetStillValid
                   && expectedOwner
                   && after.OwnerWindow != before.OwnerWindow
                ? ClipboardMutationOwnership.Owned
                : ClipboardMutationOwnership.None;
        }

        // One producer may advance the sequence several times while it empties the clipboard and
        // publishes multiple formats (Chromium does this for text, HTML, and internal metadata).
        // Attribute the completed copy by its delivered request, still-valid target, and final
        // owner instead of treating the sequence delta as a producer count.
        if (before.Sequence == 0
            || !requestDelivered
            || !targetStillValid
            || !expectedOwner)
            return ClipboardMutationOwnership.Ambiguous;

        return unchecked(after.Sequence - before.Sequence) == 1
            ? ClipboardMutationOwnership.Owned
            : ClipboardMutationOwnership.OwnedUnrestorable;
    }

    internal static bool CanReadClipboardMutation(
        ClipboardMutationOwnership ownership) =>
        ownership is ClipboardMutationOwnership.Owned
            or ClipboardMutationOwnership.OwnedUnrestorable;

    internal static bool CanRestoreCapturedClipboard(
        ClipboardMutationOwnership ownership,
        ClipboardObservation accepted,
        ClipboardObservation current) =>
        ownership == ClipboardMutationOwnership.Owned
        && CanRestoreClipboard(accepted, current);

    /// <summary>
    /// Classifies a write performed while OpenClipboard was held continuously from the
    /// pre-write observation through <paramref name="after"/>. Under that precondition, an
    /// arbitrary sequence jump cannot hide an interleaved external producer.
    /// </summary>
    internal static ClipboardMutationOwnership ClassifyLockedClipboardWrite(
        ClipboardObservation before,
        ClipboardObservation after,
        uint writerProcessId)
    {
        bool expectedOwner = after.OwnerWindow != IntPtr.Zero
                             && after.OwnerProcessId != 0
                             && after.OwnerProcessId == writerProcessId;
        if (after.Sequence == before.Sequence)
        {
            return expectedOwner && after.OwnerWindow != before.OwnerWindow
                ? ClipboardMutationOwnership.Owned
                : ClipboardMutationOwnership.None;
        }

        return expectedOwner
            ? ClipboardMutationOwnership.Owned
            : ClipboardMutationOwnership.Ambiguous;
    }

    internal static bool CanAcceptClosedClipboardWrite(
        ClipboardObservation before,
        ClipboardObservation after,
        IntPtr writerWindow,
        uint writerProcessId,
        bool clipboardClosed) =>
        clipboardClosed
        && after.OwnerWindow == writerWindow
        && after.OwnerProcessId == writerProcessId
        && ClassifyLockedClipboardWrite(before, after, writerProcessId)
           == ClipboardMutationOwnership.Owned;

    internal static bool CanRestoreClipboard(
        ClipboardObservation acceptedWrite, ClipboardObservation current) =>
        acceptedWrite.Sequence != 0
        && acceptedWrite == current;

    /// <summary>
    /// Holds the native clipboard exclusion lock continuously from the final ownership
    /// observation through the restore mutation. External producers can only commit before
    /// the observation (and be rejected) or after CloseClipboard (and remain newer).
    /// </summary>
    internal static bool TryRunLockedClipboardRestore(
        ClipboardObservation acceptedWrite,
        Func<bool> openClipboard,
        Func<ClipboardObservation> observeClipboard,
        Func<bool> restoreClipboard,
        Func<bool> closeClipboard)
    {
        if (!openClipboard()) return false;

        bool restored = false;
        bool closed = false;
        try
        {
            if (CanRestoreClipboard(acceptedWrite, observeClipboard()))
                restored = restoreClipboard();
        }
        finally
        {
            closed = closeClipboard();
        }

        return restored && closed;
    }

    internal static bool ContinuesOwnedClipboard(
        ClipboardObservation accepted,
        ClipboardObservation current,
        uint expectedOwnerProcessId) =>
        accepted.OwnerWindow != IntPtr.Zero
        && current.Sequence != 0
        && current.OwnerWindow == accepted.OwnerWindow
        && current.OwnerProcessId == expectedOwnerProcessId;

    internal static bool CanStartClipboardWrite(
        ClipboardSnapshot snapshot, ClipboardObservation current) =>
        snapshot.Observation.Sequence != 0
        && snapshot.Observation == current;

    internal static bool TryClaimClipboardMutationAtBoundary(
        SelectionOperation operation,
        ClipboardObservation expected,
        ClipboardObservation current) =>
        CanRestoreClipboard(expected, current)
        && operation.TryClaim();

    internal static ClipboardObservation ObserveClipboard()
    {
        uint sequenceBefore =
            SnapActions.Helpers.NativeMethods.GetClipboardSequenceNumber();
        IntPtr owner = GetClipboardOwner();
        uint ownerProcessId = 0;
        if (owner != IntPtr.Zero)
            GetWindowThreadProcessId(owner, out ownerProcessId);
        IntPtr ownerAfter = GetClipboardOwner();
        uint sequenceAfter =
            SnapActions.Helpers.NativeMethods.GetClipboardSequenceNumber();
        if (sequenceBefore == 0
            || sequenceBefore != sequenceAfter
            || owner != ownerAfter
            || (owner != IntPtr.Zero && ownerProcessId == 0))
            return default;
        return new ClipboardObservation(
            sequenceAfter, ownerAfter, ownerProcessId);
    }

    /// <summary>
    /// Writes paste/action text only while the operation is current and the exact pre-write
    /// clipboard observation still holds after OpenClipboard has excluded external writers.
    /// </summary>
    internal static async Task<ClipboardObservation?> TrySetClipboardTextForOperationAsync(
        SelectionOperation operation,
        ClipboardSnapshot snapshot,
        string text,
        bool requireExactTarget)
    {
        if (!snapshot.HasNativeRestorePayload) return null;
        var preparation = TryPrepareNativeClipboardWrite(
            snapshot.Observation, text);
        if (preparation == null) return null;

        NativeClipboardWriteResult nativeResult = default;
        try
        {
            bool Commit(ForegroundTarget? currentTarget)
            {
                if (!operation.IsCurrent)
                    return false;
                if (requireExactTarget)
                {
                    if (currentTarget is not { } current
                        || !ForegroundGuard.Matches(operation.Target, current)
                        || !ForegroundGuard.StillValid(operation.Target))
                        return false;
                }

                nativeResult = TryCommitPreparedClipboardWrite(
                    operation, snapshot.Observation, preparation);
                return nativeResult.Success;
            }

            bool committed = requireExactTarget
                ? await ForegroundGuard.TryRunWithExactInputTargetAsync(
                    operation.Target, current => Commit(current))
                : Commit(currentTarget: null);

            if (!committed
                && nativeResult.NeedsRollback
                && nativeResult.Observation.Sequence != 0)
            {
                await Application.Current.Dispatcher.InvokeAsync(
                    () => RestoreClipboardIfUnchanged(
                        snapshot, nativeResult.Observation));
            }

            return committed ? nativeResult.Observation : null;
        }
        finally
        {
            FreeNativeClipboardPreparation(preparation);
        }
    }

    internal static ClipboardObservation? TryCommitClipboardWrite(
        SelectionOperation operation,
        Func<ClipboardObservation?> atomicWrite)
    {
        ClipboardObservation? written = null;
        bool committed = operation.TryCommit(() =>
        {
            written = atomicWrite();
            return written != null;
        });
        return committed ? written : null;
    }

    internal static bool TryCommitClipboardMutation(
        SelectionOperation operation,
        Func<bool> mutation) =>
        operation.TryCommit(mutation);

    private static NativeClipboardWritePreparation? TryPrepareNativeClipboardWrite(
        ClipboardObservation expected, string text)
    {
        IntPtr ownerWindow = GetValidClipboardOwnerWindow();
        if (ownerWindow == IntPtr.Zero) return null;

        IntPtr textHandle = CreateUnicodeTextHandle(text);
        if (textHandle == IntPtr.Zero) return null;

        List<NativeClipboardFormatBackup>? backups = null;
        NativeClipboardWritePreparation? preparation = null;
        if (!OpenClipboard(ownerWindow))
        {
            GlobalFree(textHandle);
            return null;
        }

        try
        {
            if (CanRestoreClipboard(expected, ObserveClipboard()))
            {
                backups = DuplicateClipboardFormats();
                if (backups != null
                    && CanRestoreClipboard(expected, ObserveClipboard()))
                {
                    preparation = new NativeClipboardWritePreparation(
                        ownerWindow, textHandle, backups);
                }
            }
        }
        finally
        {
            if (!CloseClipboard())
                preparation = null;
            if (preparation == null)
            {
                GlobalFree(textHandle);
                if (backups != null)
                    FreeNativeClipboardBackups(backups);
            }
        }

        return preparation;
    }

    private static IntPtr CreateUnicodeTextHandle(string text)
    {
        byte[] bytes;
        try
        {
            bytes = new System.Text.UnicodeEncoding(
                bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true)
                .GetBytes(text + '\0');
        }
        catch { return IntPtr.Zero; }

        IntPtr memory = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);
        if (memory == IntPtr.Zero) return IntPtr.Zero;

        IntPtr destination = GlobalLock(memory);
        if (destination == IntPtr.Zero)
        {
            GlobalFree(memory);
            return IntPtr.Zero;
        }

        try
        {
            Marshal.Copy(bytes, 0, destination, bytes.Length);
        }
        catch
        {
            GlobalUnlock(memory);
            GlobalFree(memory);
            return IntPtr.Zero;
        }
        GlobalUnlock(memory);
        return memory;
    }

    private static List<NativeClipboardFormatBackup>? DuplicateClipboardFormats()
    {
        int count = CountClipboardFormats();
        var backups = new List<NativeClipboardFormatBackup>(Math.Max(count, 0));
        uint previous = 0;

        while (true)
        {
            Marshal.SetLastPInvokeError(0);
            uint format = EnumClipboardFormats(previous);
            if (format == 0)
            {
                if (Marshal.GetLastPInvokeError() == 0) return backups;
                FreeNativeClipboardBackups(backups);
                return null;
            }
            if (!CanDuplicateClipboardFormat(format))
            {
                FreeNativeClipboardBackups(backups);
                return null;
            }

            IntPtr source = GetClipboardData(format);
            IntPtr duplicate = source == IntPtr.Zero
                ? IntPtr.Zero
                : OleDuplicateData(source, checked((ushort)format), GMEM_MOVEABLE);
            if (duplicate == IntPtr.Zero)
            {
                FreeNativeClipboardBackups(backups);
                return null;
            }

            var handleKind = format is CF_BITMAP or CF_PALETTE
                ? NativeClipboardHandleKind.GdiObject
                : NativeClipboardHandleKind.GlobalMemory;
            backups.Add(new NativeClipboardFormatBackup(
                format, duplicate, handleKind));
            previous = format;
        }
    }

    private static bool CanDuplicateClipboardFormat(uint format) =>
        format <= ushort.MaxValue
        && format != CF_METAFILEPICT
        && format != CF_ENHMETAFILE
        && format != CF_OWNERDISPLAY
        && format != CF_DSPBITMAP
        && format != CF_DSPMETAFILEPICT
        && format != CF_DSPENHMETAFILE
        && (format < CF_PRIVATEFIRST || format > CF_PRIVATELAST)
        && (format < CF_GDIOBJFIRST || format > CF_GDIOBJLAST);

    private static IntPtr GetValidClipboardOwnerWindow()
    {
        IntPtr ownerWindow = Interlocked.CompareExchange(
            ref _clipboardOwnerWindow, IntPtr.Zero, IntPtr.Zero);
        if (ownerWindow == IntPtr.Zero || !IsWindow(ownerWindow))
            return IntPtr.Zero;
        GetWindowThreadProcessId(ownerWindow, out uint processId);
        return processId == (uint)Environment.ProcessId
            ? ownerWindow
            : IntPtr.Zero;
    }

    private static NativeClipboardWriteResult TryCommitPreparedClipboardWrite(
        SelectionOperation operation,
        ClipboardObservation expected,
        NativeClipboardWritePreparation preparation)
    {
        if (!IsWindow(preparation.OwnerWindow)
            || !OpenClipboard(preparation.OwnerWindow))
            return default;

        bool textTransferred = false;
        bool rollbackAttempted = false;
        bool rollbackComplete = false;
        bool clipboardClosed = false;
        try
        {
            // Final nonblocking linearization point: if a newer selection or dismissal arrived
            // during target/clipboard validation, abort before EmptyClipboard mutates anything.
            if (!TryClaimClipboardMutationAtBoundary(
                    operation, expected, ObserveClipboard()))
                return default;
            if (!EmptyClipboard())
                return default;

            IntPtr set = SetClipboardData(
                CF_UNICODETEXT, preparation.TextHandle);
            textTransferred = set != IntPtr.Zero;
            if (textTransferred)
            {
                preparation.TextHandle = IntPtr.Zero;
            }
            else
            {
                // The clipboard is already empty. Restore every pre-duplicated format while
                // the exclusion lock is still held so an external writer cannot interleave.
                rollbackAttempted = true;
                rollbackComplete = RestoreNativeClipboardBackups(
                    preparation.Backups);
            }
        }
        finally
        {
            clipboardClosed = CloseClipboard();
        }

        // Ownership sampled while the clipboard is open is only tentative: a producer can win
        // immediately after CloseClipboard. This post-close sample is the token callers use for
        // paste and any managed fallback restore.
        var after = ObserveClipboard();
        bool stillOwnsClipboard =
            after.OwnerWindow == preparation.OwnerWindow
            && after.OwnerProcessId == (uint)Environment.ProcessId;

        if (textTransferred)
        {
            bool accepted = CanAcceptClosedClipboardWrite(
                expected,
                after,
                preparation.OwnerWindow,
                (uint)Environment.ProcessId,
                clipboardClosed);
            return new NativeClipboardWriteResult(
                Success: accepted,
                NeedsRollback: !accepted && stillOwnsClipboard,
                after);
        }

        // A complete inline rollback already restored all duplicated formats. If it was partial,
        // only the still-current app-owned observation is eligible for the richer managed
        // fallback; a foreign post-close writer must be preserved.
        return new NativeClipboardWriteResult(
            Success: false,
            NeedsRollback: rollbackAttempted
                           && !rollbackComplete
                           && stillOwnsClipboard,
            after);
    }

    internal static bool RestoreNativeClipboardBackups(
        List<NativeClipboardFormatBackup> backups,
        Func<uint, IntPtr, IntPtr>? setClipboardData = null)
    {
        bool restored = true;
        foreach (var backup in backups)
        {
            if (backup.Handle == IntPtr.Zero) continue;
            IntPtr set = setClipboardData != null
                ? setClipboardData(backup.Format, backup.Handle)
                : SetClipboardData(backup.Format, backup.Handle);
            if (set == IntPtr.Zero)
            {
                restored = false;
                continue;
            }
            backup.Handle = IntPtr.Zero;
        }
        return restored;
    }

    internal static bool TryReplaceClipboardContentsUnderLock(
        Func<bool> emptyClipboard,
        Func<bool> restoreDesired,
        Func<bool> restoreRollback)
    {
        if (!emptyClipboard()) return false;
        if (restoreDesired()) return true;

        if (emptyClipboard())
            restoreRollback();
        return false;
    }

    private static void FreeNativeClipboardPreparation(
        NativeClipboardWritePreparation preparation)
    {
        if (preparation.TextHandle != IntPtr.Zero)
        {
            GlobalFree(preparation.TextHandle);
            preparation.TextHandle = IntPtr.Zero;
        }
        FreeNativeClipboardBackups(preparation.Backups);
    }

    private static void FreeNativeClipboardBackups(
        List<NativeClipboardFormatBackup> backups)
    {
        foreach (var backup in backups)
        {
            if (backup.Handle == IntPtr.Zero) continue;
            if (backup.HandleKind == NativeClipboardHandleKind.GdiObject)
                DeleteObject(backup.Handle);
            else
                GlobalFree(backup.Handle);
            backup.Handle = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Eagerly reads the managed clipboard formats used by actions, then duplicates every native
    /// format for lossless restoration. Managed-only custom formats are deferred to that native
    /// backup instead of rejecting common Chromium clipboards before duplication is attempted.
    /// A failed required managed read, native duplication, or concurrent write rejects the snapshot.
    /// </summary>
    internal static ClipboardSnapshot? SnapshotClipboard()
    {
        try
        {
            var observationBefore = ObserveClipboard();
            var data = Clipboard.GetDataObject();
            if (data == null && CountClipboardFormats() != 0)
                return null;
            var snap = new Dictionary<string, object>();
            var reads = new List<ClipboardFormatRead>();

            if (data != null)
            {
                foreach (var fmt in data.GetFormats(autoConvert: false))
                {
                    if (!RoundTrippableFormats.Contains(fmt))
                    {
                        reads.Add(new ClipboardFormatRead(fmt, ReadSucceeded: false, HasValue: false));
                        continue;
                    }

                    try
                    {
                        var obj = data.GetData(fmt, autoConvert: false);
                        reads.Add(new ClipboardFormatRead(
                            fmt, ReadSucceeded: true, HasValue: obj != null));
                        if (obj != null) snap[fmt] = obj;
                    }
                    catch
                    {
                        reads.Add(new ClipboardFormatRead(
                            fmt, ReadSucceeded: false, HasValue: false));
                    }
                }
            }

            var observation = ObserveClipboard();
            if (!IsCompleteSnapshot(observationBefore, observation, reads))
                return null;

            var nativeBackups = TryCaptureNativeClipboardBackups(observation);
            return nativeBackups == null
                ? null
                : new ClipboardSnapshot(snap, observation, nativeBackups);
        }
        catch
        {
            return null;
        }
    }

    private static List<NativeClipboardFormatBackup>?
        TryCaptureNativeClipboardBackups(ClipboardObservation expected)
    {
        IntPtr ownerWindow = GetValidClipboardOwnerWindow();
        if (ownerWindow == IntPtr.Zero || !OpenClipboard(ownerWindow))
            return null;

        List<NativeClipboardFormatBackup>? backups = null;
        bool stable = false;
        bool closed;
        try
        {
            if (CanRestoreClipboard(expected, ObserveClipboard()))
            {
                backups = DuplicateClipboardFormats();
                stable = backups != null
                         && CanRestoreClipboard(expected, ObserveClipboard());
            }
        }
        finally
        {
            closed = CloseClipboard();
        }

        if (stable && closed) return backups;
        if (backups != null) FreeNativeClipboardBackups(backups);
        return null;
    }

    /// <summary>
    /// Consumes the snapshot's one-shot native payload and restores it only while the exact
    /// accepted write is still current under one OpenClipboard lock.
    /// </summary>
    internal static bool RestoreClipboardIfUnchanged(
        ClipboardSnapshot snapshot,
        ClipboardObservation acceptedWrite) =>
        RestoreClipboardIfUnchanged(snapshot, acceptedWrite, NativeClipboard);

    internal static bool RestoreClipboardIfUnchanged(
        ClipboardSnapshot snapshot,
        ClipboardObservation acceptedWrite,
        ClipboardNativeApi nativeClipboard)
    {
        List<NativeClipboardFormatBackup>? original =
            snapshot.TakeNativeBackups();
        List<NativeClipboardFormatBackup>? rollback = null;
        try
        {
            if (original == null) return false;
            IntPtr ownerWindow = nativeClipboard.GetOwnerWindow();
            if (ownerWindow == IntPtr.Zero) return false;

            return TryRunLockedClipboardRestore(
                acceptedWrite,
                openClipboard: () => nativeClipboard.Open(ownerWindow),
                observeClipboard: nativeClipboard.Observe,
                restoreClipboard: () =>
                {
                    if (original.Count == 0)
                        return nativeClipboard.Empty();

                    // Preserve the temporary clipboard as rollback material before EmptyClipboard.
                    // Format reads can force delayed rendering, so recheck the exact accepted
                    // observation after duplication and before the first mutation.
                    rollback = nativeClipboard.DuplicateFormats();
                    if (rollback == null
                        || !CanRestoreClipboard(
                            acceptedWrite, nativeClipboard.Observe()))
                        return false;

                    // A failed SetClipboardData may leave a partial original. Remove it while the
                    // lock is still held and put back the pre-mutation temporary clipboard.
                    return TryReplaceClipboardContentsUnderLock(
                        emptyClipboard: nativeClipboard.Empty,
                        restoreDesired: () =>
                            nativeClipboard.RestoreFormats(original),
                        restoreRollback: () =>
                            nativeClipboard.RestoreFormats(rollback));
                },
                closeClipboard: nativeClipboard.Close);
        }
        catch
        {
            return false;
        }
        finally
        {
            if (original != null) FreeNativeClipboardBackups(original);
            if (rollback != null) FreeNativeClipboardBackups(rollback);
            snapshot.Dispose();
        }
    }

    /// <summary>
    /// Reads whatever text is already on the clipboard, with no clear / synthetic-copy dance. Used
    /// by the opt-in "capture on real Ctrl+C" trigger, where the user has already copied the text —
    /// so there is zero clipboard mutation and nothing for other apps to observe.
    /// </summary>
    public static Task<string?> ReadCurrentClipboardTextAsync() => ReadClipboard();

    private static async Task<string?> ReadClipboard()
    {
        return await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
            catch { return null; }
        });
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardOwner();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll")]
    private static extern int CountClipboardFormats();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint EnumClipboardFormats(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("ole32.dll")]
    private static extern IntPtr OleDuplicateData(
        IntPtr hSrc, ushort cfFormat, uint uiFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);
}
