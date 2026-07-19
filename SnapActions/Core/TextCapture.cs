using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;

namespace SnapActions.Core;

public static class TextCapture
{
    private const int INPUT_KEYBOARD = 1;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU = 0x12;    // Alt
    private const ushort VK_INSERT = 0x2D;  // Ctrl+Insert = Copy / Shift+Insert = Paste
    private const ushort VK_DELETE = 0x2E;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint WM_COPY = 0x0301;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint WM_COPY_TIMEOUT_MS = 100;
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

    private static readonly KeyStroke[] CtrlInsertInputs = BuildExtendedInsertCombo(VK_CONTROL);
    private static readonly KeyStroke[] ShiftInsertInputs = BuildExtendedInsertCombo(VK_SHIFT);
    private static readonly KeyStroke[] DeleteInputs =
    [
        new(VK_DELETE, KeyUp: false, Extended: true),
        new(VK_DELETE, KeyUp: true, Extended: true),
    ];
    private static readonly int InputSize = Marshal.SizeOf<INPUT>();
    private static IntPtr _clipboardOwnerWindow;

    // Serialize captures so rapid selections can't interleave snapshot/restore and corrupt the
    // clipboard. Contenders queue, then validate their immutable operation after acquisition:
    // stale captures exit while the newest one is allowed to proceed.
    private static readonly System.Threading.SemaphoreSlim _captureLock = new(1, 1);

    // Clipboard formats we can read eagerly and preserve as native handles. If even one advertised
    // managed format or native handle cannot be captured safely, the snapshot is incomplete and
    // every clipboard-mutating capture fallback is disabled; UIA may still capture without mutation.
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
        Owned,
        Ambiguous,
    }

    internal enum InputInjectionStatus
    {
        Rejected,
        Succeeded,
        Partial,
    }

    internal readonly record struct InputInjectionOutcome(
        InputInjectionStatus Status,
        bool CleanupSucceeded = true,
        uint AcceptedCount = 0);

    internal readonly record struct KeyStroke(
        ushort VirtualKey,
        bool KeyUp,
        bool Extended);

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
            RoundTrippableFormats.Contains(read.Format)
            && read.ReadSucceeded
            && read.HasValue);

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

        return requestDelivered
               && unchecked(after.Sequence - before.Sequence) == 1
               && targetStillValid
               && expectedOwner
            ? ClipboardMutationOwnership.Owned
            : ClipboardMutationOwnership.Ambiguous;
    }

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

    internal static bool CanInjectAtBoundary(
        bool operationCurrent,
        ForegroundTarget expectedTarget,
        ForegroundTarget currentTarget,
        ClipboardObservation? expectedClipboard,
        ClipboardObservation currentClipboard) =>
        operationCurrent
        && ForegroundGuard.HasSufficientInputIdentity(expectedTarget)
        && ForegroundGuard.Matches(expectedTarget, currentTarget)
        && (expectedClipboard == null
            || CanRestoreClipboard(expectedClipboard.Value, currentClipboard));

    internal static bool CanRollbackAfterPartialPaste(
        InputInjectionOutcome outcome) =>
        outcome.Status == InputInjectionStatus.Partial
        && outcome.CleanupSucceeded
        && outcome.AcceptedCount < 2;

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
    /// Captures the current selection. <paramref name="isDrag"/> distinguishes a drag gesture
    /// (strongest selection intent — both the I-beam and drag-distance gates agreed) from a
    /// multi-click; <paramref name="allowSyntheticKeys"/> is false for quiet-only captures
    /// (<see cref="CaptureAggressiveness.Quiet"/>) where a Ctrl+Insert must never be injected;
    /// <paramref name="ambiguousCursor"/> is true when the gesture ran under the arrow/hand cursor
    /// at both ends — it withholds WM_COPY on an Unknown UIA outcome so an Explorer row seen during
    /// a UIA timeout can't have its filename copied (see <see cref="DecidePlan"/>);
    /// <paramref name="cursorX"/>/<paramref name="cursorY"/> are the gesture point, used by the UIA
    /// pre-gate to rescue selectable text inside item containers (X/Twitter feed tweets).
    /// </summary>
    internal static async Task<string?> CaptureSelectedTextAsync(
        SelectionOperation operation,
        bool isDrag,
        bool allowSyntheticKeys,
        bool ambiguousCursor,
        int cursorX,
        int cursorY)
    {
        // Queue behind an older capture, then discard whichever token is stale after acquisition.
        // Dropping the contender here would lose the newer selection while the stale one still ran.
        await _captureLock.WaitAsync();
        ClipboardSnapshot? saved = null;
        ClipboardObservation? acceptedWrite = null;
        try
        {
            if (!await operation.CanInjectInputAsync()) return null;

            // A drag under the ambiguous arrow/hand cursor is a strong selection signal (unlike a
            // click) whose text UIA and WM_COPY often can't read in Chromium (the X/Twitter feed).
            // For it we let the self-gating Ctrl+Insert keystroke run even past an item-suppress.
            // Gated on allowSyntheticKeys so the caller's Explorer/file-manager exclusion (where a
            // keystroke would copy files, not text) also disables the item-suppress override.
            bool aggressiveDrag = ambiguousCursor && isDrag && allowSyntheticKeys;

            // UIA pre-gate. Outcomes:
            //   HasText            — a real text selection; use it, skip the whole clipboard dance.
            //   SuppressItemElement — focus is a non-text item (Explorer file, desktop icon, list
            //                         row). Bail out: WM_COPY against those "succeeds" by copying
            //                         the filename/item text even though no text is selected.
            //   EmptyTextPattern   — TextPattern present but reports no selection. NOT definitive:
            //                         some providers return empty selections even when the user
            //                         clearly has text selected (the same app class Ctrl+Insert
            //                         exists for), so DecidePlan continues with a restricted
            //                         cascade instead of bailing out — see that method.
            //   Unknown            — UIA can't tell (no TextPattern, exception, shallow tree).
            // Skip UIA entirely for apps whose accessibility provider is known to hang/misbehave in
            // TextPattern — go straight to the clipboard path for them.
            bool skipUia = UiaSkipApps.Contains(ForegroundApp.GetActiveProcessName() ?? "");
            var outcome = SelectionProbeOutcome.Unknown;
            if (!skipUia)
            {
                // Bounded (RunBoundedUiaAsync) so a wedged provider can't hang here and permanently
                // hold the capture lock.
                var probe = await RunBoundedUiaAsync(
                    () => ProbeSelectionViaUIA(
                        cursorX, cursorY, preferKeystroke: allowSyntheticKeys,
                        operation.Target.ProcessId,
                        operation.Target.AutomationRuntimeId),
                    new SelectionProbe(SelectionProbeOutcome.Unknown, null, "UIA pre-gate timed out"));
                if (!await operation.CanInjectInputAsync()) return null;
                if (probe.Outcome == SelectionProbeOutcome.HasText)
                {
                    SnapActions.Helpers.Log.Info($"UIA pre-gate returned text ({probe.Text!.Length} chars) — skipping clipboard pipeline");
                    return probe.Text;
                }
                outcome = probe.Outcome;
                if (outcome == SelectionProbeOutcome.SuppressItemElement && !aggressiveDrag)
                {
                    SnapActions.Helpers.Log.Info($"UIA pre-gate suppressed capture: {probe.Reason}");
                    return null;
                }
                // For an ambiguous-cursor drag we deliberately DON'T bail on an item-suppress:
                // X/Twitter exposes each feed tweet as a ListItem+SelectionItemPattern container of
                // selectable text, so the item signal is a false stop. The self-gating keystroke
                // cascade below reads the real selection if there is one, nothing if there isn't.
                if (outcome == SelectionProbeOutcome.EmptyTextPattern)
                    SnapActions.Helpers.Log.Info($"UIA pre-gate saw an empty TextPattern ({probe.Reason}) — continuing with restricted cascade");
            }

            var plan = DecidePlan(outcome, isDrag, allowSyntheticKeys, ambiguousCursor);
            if (skipUia) plan = plan with { RunUia = false };
            if (!plan.RunWmCopy && !plan.RunUia && !plan.RunKeystroke) return null;

            // Clipboard-mutating fallbacks are permitted only with a complete, stable snapshot.
            // Unsupported or delay-rendered formats make capture UIA-only rather than risking loss.
            if (plan.RunWmCopy || plan.RunKeystroke)
            {
                saved = await Application.Current.Dispatcher.InvokeAsync(SnapshotClipboard);
                if (!await operation.CanInjectInputAsync()) return null;
                if (saved == null)
                    plan = plan with { RunWmCopy = false, RunKeystroke = false };
            }

            if (!plan.RunWmCopy && !plan.RunUia && !plan.RunKeystroke) return null;

            string? text = null;
            bool ambiguousClipboardChange = false;

            if (plan.RunWmCopy && saved != null)
            {
                var before = ObserveClipboard();
                if (before != saved.Observation
                    || !await operation.CanInjectInputAsync())
                {
                    ambiguousClipboardChange = true;
                }
                else
                {
                    bool delivered = await TryRunClipboardMutationAsync(
                        operation,
                        before,
                        () => CopyViaWindowMessage(operation.Target));
                    await Task.Delay(20);
                    var after = ObserveClipboard();
                    bool targetStillValid = operation.IsCurrent
                                            && await ForegroundGuard.StillValidAsync(
                                                operation.Target);
                    var ownership = ClassifyClipboardMutation(
                        before, after, delivered, operation.Target.ProcessId,
                        targetStillValid);
                    if (ownership == ClipboardMutationOwnership.Owned)
                    {
                        text = await ReadClipboard();
                        var afterRead = ObserveClipboard();
                        if (ContinuesOwnedClipboard(
                                after, afterRead, operation.Target.ProcessId))
                        {
                            acceptedWrite = afterRead;
                        }
                        else
                        {
                            text = null;
                            ambiguousClipboardChange = true;
                        }
                    }
                    else if (ownership == ClipboardMutationOwnership.Ambiguous)
                    {
                        ambiguousClipboardChange = true;
                    }
                }
            }

            // UI Automation is safe even when the clipboard snapshot was incomplete or another
            // process changed the clipboard; accept its result only while this operation is current.
            if (string.IsNullOrEmpty(text) && plan.RunUia && operation.CanInjectInput)
            {
                text = await RunBoundedUiaAsync(
                    () => CopyViaUIA(
                        operation.Target.ProcessId,
                        operation.Target.AutomationRuntimeId),
                    null);
                if (!await operation.CanInjectInputAsync()) return null;
            }

            // Last resort: Ctrl+Insert. Stop after any ambiguous clipboard change; issuing another
            // copy would compound uncertainty about whose data is currently on the clipboard.
            if (string.IsNullOrEmpty(text)
                && plan.RunKeystroke
                && saved != null
                && acceptedWrite == null
                && !ambiguousClipboardChange)
            {
                var before = ObserveClipboard();
                if (before != saved.Observation)
                {
                    ambiguousClipboardChange = true;
                }
                else if (await WaitForModifierKeysReleasedAsync(
                             VK_SHIFT, VK_CONTROL, VK_MENU)
                         && await operation.CanInjectInputAsync())
                {
                    // A user copy during the modifier wait invalidates the transaction before input.
                    before = ObserveClipboard();
                    if (before != saved.Observation)
                    {
                        ambiguousClipboardChange = true;
                    }
                    else
                    {
                        var inputOutcome = await TrySendInputAsync(
                            operation,
                            before,
                            CtrlInsertInputs,
                            VK_SHIFT, VK_CONTROL, VK_MENU);
                        if (inputOutcome.Status == InputInjectionStatus.Partial)
                        {
                            SnapActions.Helpers.Log.Warn(
                                inputOutcome.CleanupSucceeded
                                    ? "Ctrl+Insert was only partially inserted; capture ownership is ambiguous"
                                    : "Ctrl+Insert was partially inserted and key-up cleanup was incomplete");
                            ambiguousClipboardChange = true;
                        }
                        else
                        {
                            bool delivered =
                                inputOutcome.Status == InputInjectionStatus.Succeeded;
                            for (int i = 0; i < 25; i++)
                            {
                                await Task.Delay(10);
                                var after = ObserveClipboard();
                                if (after == before) continue;
                                bool targetStillValid = operation.IsCurrent
                                                        && await ForegroundGuard.StillValidAsync(
                                                            operation.Target);
                                var ownership = ClassifyClipboardMutation(
                                    before, after, delivered, operation.Target.ProcessId,
                                    targetStillValid);
                                if (ownership == ClipboardMutationOwnership.Owned)
                                {
                                    text = await ReadClipboard();
                                    var afterRead = ObserveClipboard();
                                    if (ContinuesOwnedClipboard(
                                            after, afterRead, operation.Target.ProcessId))
                                    {
                                        acceptedWrite = afterRead;
                                    }
                                    else
                                    {
                                        text = null;
                                        ambiguousClipboardChange = true;
                                    }
                                }
                                else
                                {
                                    ambiguousClipboardChange = true;
                                }
                                break;
                            }
                        }
                    }
                }
            }

            return await operation.CanInjectInputAsync() ? text : null;
        }
        catch (Exception ex)
        {
            SnapActions.Helpers.Log.Error("Capture error", ex);
            return null;
        }
        finally
        {
            if (saved != null && acceptedWrite is { } ownedWrite)
            {
                await Application.Current.Dispatcher.InvokeAsync(
                    () => RestoreClipboardIfUnchanged(saved, ownedWrite));
            }
            saved?.Dispose();
            _captureLock.Release();
        }
    }

    // Apps whose UIA TextPattern provider is known to hang or misbehave — skip UIA for them and use
    // the clipboard path directly. Process name, no .exe.
    private static readonly HashSet<string> UiaSkipApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "thunderbird",
    };

    private const int UiaCallTimeoutMs = 500;

    /// <summary>
    /// Runs a UIA call with a hard timeout and a shared pre-start single-flight gate. If a broken
    /// provider blocks inside GetSelection/GetText, the await returns its fallback but the gate
    /// stays occupied until that underlying call really exits. Later UIA calls fail fast instead
    /// of accumulating more stranded workers.
    /// </summary>
    private static Task<T> RunBoundedUiaAsync<T>(
        Func<T> uiaCall, T onTimeout) =>
        ForegroundGuard.RunBoundedAutomationAsync(
            uiaCall, onTimeout, UiaCallTimeoutMs);

    /// <summary>
    /// Waits up to ~300 ms for the user to release the given modifier keys before we inject a
    /// synthetic chord. A modifier still held at gesture end (Shift+drag to extend a selection,
    /// Ctrl+drag for a discontiguous one) would otherwise corrupt the chord — Ctrl+Insert into
    /// Ctrl+Shift+Insert, Shift+Insert into Ctrl+Shift+Insert — which copies/pastes nothing in
    /// many apps. Destructive and clipboard-mutating chords require Shift, Ctrl, and Alt all to
    /// be released so our synthetic key-up cannot interfere with a physically held modifier.
    /// </summary>
    private static async Task<bool> WaitForModifierKeysReleasedAsync(params int[] vkeys)
    {
        for (int i = 0; i < 15; i++)
        {
            if (AreModifierKeysReleased(vkeys)) return true;
            await Task.Delay(20);
        }
        return false;
    }

    private static bool AreModifierKeysReleased(params int[] vkeys)
    {
        foreach (var vk in vkeys)
        {
            if ((SnapActions.Helpers.NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Eagerly snapshots every advertised clipboard format that can be round-tripped safely.
    /// Any unsupported/failed format or concurrent clipboard write rejects the whole snapshot;
    /// callers must then avoid clipboard-mutating capture fallbacks.
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

    /// <summary>
    /// Maximum UIA parent levels to walk when probing for a TextPattern. Same rationale as
    /// <see cref="ForegroundApp.IsTextInputAtPoint"/>: leaf elements (a span / anchor / svg)
    /// usually don't expose TextPattern themselves even though the paragraph / document
    /// ancestor does.
    /// </summary>
    private const int TextPatternParentWalkDepth = 6;

    internal enum SelectionProbeOutcome
    {
        /// <summary>UIA gave us the selected text directly — use it and skip the clipboard pipeline.</summary>
        HasText,
        /// <summary>The focused element is a non-text item (Explorer file, desktop icon, list row).
        /// Definitive — capture must not run (WM_COPY would copy the item's name).</summary>
        SuppressItemElement,
        /// <summary>A TextPattern was found but reported an empty selection. Usually means "no
        /// selection", but some providers lie (report empty despite a real selection), so this is
        /// a restriction signal, not a hard stop — see <see cref="DecidePlan"/>.</summary>
        EmptyTextPattern,
        /// <summary>UIA couldn't determine. Fall through to WM_COPY / Ctrl+Insert.</summary>
        Unknown,
    }

    internal readonly record struct SelectionProbe(SelectionProbeOutcome Outcome, string? Text, string? Reason);

    /// <summary>Which capture layers a given gesture may run. Produced by <see cref="DecidePlan"/>.</summary>
    internal readonly record struct CapturePlan(bool RunWmCopy, bool RunUia, bool RunKeystroke);

    /// <summary>
    /// Pure policy: probe outcome × gesture → which layers run. The balance being struck:
    /// <list type="bullet">
    ///   <item><b>EmptyTextPattern + drag</b> — full cascade (keystroke allowed). A drag that
    ///     passed the I-beam and distance gates is the strongest possible selection signal; a
    ///     provider reporting "empty" against it is exactly the lying-provider class the
    ///     Ctrl+Insert fallback exists for. If there genuinely is no selection, Ctrl+Insert
    ///     copies nothing and the sequence number stays put — self-gating.</item>
    ///   <item><b>EmptyTextPattern + multi-click</b> — WM_COPY only. Double-click is the gesture
    ///     most prone to non-text false positives, so no synthetic keystroke; but WM_COPY is
    ///     silent and a no-op when nothing is selected, so lying providers that answer it still
    ///     get their toolbar. UIA is skipped — the probe just walked the same tree and came back
    ///     empty.</item>
    ///   <item><b>Unknown</b> — normal cascade, capped by the caller's aggressiveness (a quiet
    ///     capture never injects keys regardless of outcome) — EXCEPT when
    ///     <paramref name="ambiguousCursor"/> is set (the gesture ran under arrow/hand at both
    ///     ends). Then Unknown runs nothing: arrow/hand with no UIA-confirmable text is almost
    ///     always a genuine non-text item (an Explorer row caught during a UIA timeout), and
    ///     WM_COPY there would copy the item's name and pop a spurious toolbar — the false
    ///     positive the cursor gate's old hard-suppress prevented. Real web text yields
    ///     HasText/EmptyTextPattern (never Unknown), so the arrow/hand selection fix is untouched;
    ///     custom-cursor quiet captures (Unknown cursor kind, not ambiguous) keep the WM_COPY
    ///     fallback their custom-I-beam editors rely on.</item>
    /// </list>
    /// (HasText / SuppressItemElement are resolved before planning.)
    /// </summary>
    internal static CapturePlan DecidePlan(SelectionProbeOutcome outcome, bool isDrag,
        bool allowSyntheticKeys, bool ambiguousCursor = false)
    {
        // An ambiguous arrow/hand DRAG (not a click) is a strong selection signal; allowSyntheticKeys
        // is set for it by the caller (and cleared for Explorer/file managers). Its text is often
        // invisible to UIA/WM_COPY (X/Twitter feed), so the Ctrl+Insert keystroke is the reliable
        // capture — run the full cascade even on an item-suppress (a feed tweet is a ListItem that
        // holds text). Self-gating: no selection ⇒ sequence number doesn't move ⇒ nothing captured.
        bool aggressiveDrag = ambiguousCursor && isDrag && allowSyntheticKeys;
        return outcome switch
        {
            SelectionProbeOutcome.SuppressItemElement => aggressiveDrag
                ? new(true, true, true)
                : new(false, false, false),
            SelectionProbeOutcome.EmptyTextPattern => new(true, false, isDrag && allowSyntheticKeys),
            // Unknown: Full or ambiguous-drag (allowSyntheticKeys true) runs the keystroke cascade;
            // an ambiguous multi-click (no keys) stays silent to avoid a WM_COPY-on-item false
            // positive; a custom-cursor quiet capture keeps its WM_COPY fallback for editors.
            _ when allowSyntheticKeys => new(true, true, true),
            _ when ambiguousCursor => new(false, false, false),
            _ => new(true, true, false),
        };
    }

    /// <summary>
    /// Item-style control types that are NOT text. When the focused element is one of these
    /// AND exposes SelectionItemPattern AND we found no TextPattern up the tree, we treat the
    /// "selection" as an item selection (file in Explorer, desktop icon, list-box row, tree
    /// node) and suppress. Deliberately narrow — Pane / Custom / Document stay out because
    /// browsers and Electron focus those for real text contexts.
    /// </summary>
    private static readonly System.Windows.Automation.ControlType[] NonTextItemTypes =
    [
        System.Windows.Automation.ControlType.DataItem,
        System.Windows.Automation.ControlType.ListItem,
        System.Windows.Automation.ControlType.TreeItem,
    ];

    /// <summary>
    /// Probes UI Automation to decide whether a real text selection exists right now. Layered
    /// gate to prevent the WM_COPY pipeline from misreading non-text contexts (Explorer file
    /// double-click, desktop icon, list row) as text selections. Runs on a worker thread —
    /// UIA calls can take 50–500 ms cold.
    /// </summary>
    /// <remarks>
    /// Three UIA-based gates were tried and removed across v1.6.5–1.6.12 because they over-
    /// suppressed legitimate selections. This one is more conservative: only a clearly non-text
    /// item element (SuppressItemElement) is a hard stop. An explicitly-empty TextPattern is a
    /// *restriction* signal (EmptyTextPattern), not a stop — some providers report an empty
    /// selection even when the user clearly has text selected, so <see cref="DecidePlan"/> keeps
    /// a reduced cascade alive for it. Anything ambiguous (no TextPattern, exception, shallow
    /// tree) returns Unknown, which leaves the full WM_COPY → Ctrl+Insert fallback intact.
    ///
    /// <paramref name="cursorX"/>/<paramref name="cursorY"/> are the gesture point, used to read the
    /// selection from the element UNDER THE CURSOR when the walk from focus finds nothing — some
    /// containers hold selectable text but focus lands on the container (X/Twitter exposes each feed
    /// tweet as a ListItem, focused, not the text). That under-cursor read is skipped when
    /// <paramref name="preferKeystroke"/> is set (a drag / Full capture, where the Ctrl+Insert
    /// keystroke is available): GetSelection() misreads bidirectional (mixed LTR/RTL) selections —
    /// FromPoint lands on an adjacent Arabic run and returns its text, not the selected Latin word —
    /// whereas the keystroke copies exactly what the browser shows selected. So the rescue is only a
    /// fallback for gestures with no keystroke (ambiguous multi-click / quiet custom-cursor capture).
    /// </remarks>
    internal static SelectionProbe ProbeSelectionViaUIA(
        int cursorX,
        int cursorY,
        bool preferKeystroke,
        uint expectedProcessId,
        string? expectedRuntimeId)
    {
        AutomationElement? originalFocused = null;
        try
        {
            originalFocused = AutomationElement.FocusedElement;
            if (originalFocused == null)
                return new SelectionProbe(SelectionProbeOutcome.Unknown, null, "no focused element");
            if ((uint)originalFocused.Current.ProcessId != expectedProcessId)
                return new SelectionProbe(
                    SelectionProbeOutcome.Unknown, null, "focused element belongs to another process");
            if (!MatchesAutomationRuntimeId(originalFocused, expectedRuntimeId))
                return new SelectionProbe(
                    SelectionProbeOutcome.Unknown, null, "focused element identity changed");

            // Walk up looking for TextPattern. If ANY ancestor has TextPattern with non-empty
            // selection → HasText (return immediately). If we exhaust the walk and saw at least
            // one TextPattern but all were empty → Suppress. If we never saw TextPattern → fall
            // through to the item-element check below.
            var walker = TreeWalker.RawViewWalker;
            var element = originalFocused;
            bool sawAnyTextPattern = false;
            for (int depth = 0; element != null && depth < TextPatternParentWalkDepth; depth++)
            {
                try
                {
                    if (element.TryGetCurrentPattern(TextPattern.Pattern, out var pat))
                    {
                        sawAnyTextPattern = true;
                        var tp = (TextPattern)pat;
                        var ranges = tp.GetSelection();
                        if (ranges != null && ranges.Length > 0)
                        {
                            var combined = ranges.Length == 1
                                ? ranges[0].GetText(-1)
                                : string.Join("\n",
                                    ranges.Select(r => r.GetText(-1)).Where(s => !string.IsNullOrEmpty(s)));
                            if (!string.IsNullOrEmpty(combined))
                                return new SelectionProbe(SelectionProbeOutcome.HasText, combined, null);
                        }
                        // TextPattern at this level returned no selection text. Keep walking up
                        // — an ancestor pane / document may have the real selection (browsers
                        // often expose TextPattern at multiple levels with the leaf empty).
                    }
                }
                catch { /* per-level UIA failure — try the parent */ }

                try { element = walker.GetParent(element); }
                catch { break; }
            }

            if (sawAnyTextPattern)
                return new SelectionProbe(SelectionProbeOutcome.EmptyTextPattern,
                    null, "TextPattern present but selection is empty");

            // No TextPattern anywhere up the walk from FOCUS. Before classifying, read the
            // selection from the element UNDER THE CURSOR: X/Twitter focuses the tweet container
            // (a ListItem — or, inconsistently, a plain group), not the text, so the upward walk
            // from focus misses the tweet's own text, which sits right under the cursor. Covers
            // both the item case AND the plain-Unknown case.
            // Read the selection from the element UNDER THE CURSOR — X/Twitter focuses the tweet
            // container, not the text, so the walk from focus missed it. BUT GetSelection() is
            // unreliable for bidirectional (mixed LTR/RTL) content: FromPoint lands on an adjacent
            // Arabic run and returns ITS text, not the visually-selected Latin word (confirmed:
            // a "literacy" drag read back 8 Arabic chars). So use this rescue ONLY when the
            // reliable Ctrl+Insert keystroke isn't available (an ambiguous multi-click, or a quiet
            // custom-cursor capture). When it IS available — a drag or a Full capture — we skip the
            // rescue and let the keystroke copy exactly what the browser shows selected.
            if (!preferKeystroke)
            {
                var atPoint = TryReadSelectionAtPoint(cursorX, cursorY, expectedProcessId);
                if (!string.IsNullOrEmpty(atPoint))
                    return new SelectionProbe(SelectionProbeOutcome.HasText, atPoint,
                        "rescued selection under cursor");
            }

            // Layer C: check the originally-focused element for non-text item patterns —
            // Explorer file rows, desktop icons, list-box rows. SelectionItemPattern means
            // "I am a selectable item" (vs. text); ControlType keeps us off Pane / Custom /
            // Document which browsers and Electron focus for real text contexts.
            try
            {
                var ct = originalFocused.Current.ControlType;
                if (NonTextItemTypes.Contains(ct)
                    && originalFocused.TryGetCurrentPattern(SelectionItemPattern.Pattern, out _))
                    return new SelectionProbe(SelectionProbeOutcome.SuppressItemElement,
                        null, $"focused element is {ct.ProgrammaticName} with SelectionItemPattern");
            }
            catch { /* couldn't read ControlType — fall through to Unknown */ }

            return new SelectionProbe(SelectionProbeOutcome.Unknown, null, "no TextPattern, not a known non-text item");
        }
        catch (Exception ex)
        {
            // Total UIA failure — be permissive (fall through to clipboard pipeline) so we
            // don't silently break selections in apps where UIA misbehaves.
            return new SelectionProbe(SelectionProbeOutcome.Unknown, null, $"UIA exception: {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// Reads a non-empty text selection from the element under (<paramref name="x"/>,
    /// <paramref name="y"/>) — walking up a few levels for the TextPattern the way the feed's
    /// tweet text exposes it a level or two above the leaf under the cursor. Returns null when
    /// there's no selection there (an Explorer file row, a desktop icon, a bare button). Runs on
    /// the same worker thread as <see cref="ProbeSelectionViaUIA"/>; must not throw.
    /// </summary>
    private static string? TryReadSelectionAtPoint(int x, int y, uint expectedProcessId)
    {
        try
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(x, y));
            if (element == null) return null;
            if ((uint)element.Current.ProcessId != expectedProcessId) return null;
            var walker = TreeWalker.RawViewWalker;
            for (int depth = 0; element != null && depth < TextPatternParentWalkDepth; depth++)
            {
                try
                {
                    if (element.TryGetCurrentPattern(TextPattern.Pattern, out var pat))
                    {
                        var ranges = ((TextPattern)pat).GetSelection();
                        if (ranges != null && ranges.Length > 0)
                        {
                            var combined = ranges.Length == 1
                                ? ranges[0].GetText(-1)
                                : string.Join("\n",
                                    ranges.Select(r => r.GetText(-1)).Where(s => !string.IsNullOrEmpty(s)));
                            if (!string.IsNullOrEmpty(combined)) return combined;
                        }
                    }
                }
                catch { /* per-level UIA failure — try the parent */ }

                try { element = walker.GetParent(element); }
                catch { break; }
            }
        }
        catch { /* FromPoint / UIA failure — no rescue */ }
        return null;
    }

    /// <summary>
    /// Reads the current selection via UI Automation. Returns null when no focused element,
    /// no TextPattern within the walk depth, no selection ranges, or any UIA failure. Runs on
    /// a worker thread because UIA calls can take hundreds of ms in apps where a11y is cold.
    /// </summary>
    /// <remarks>
    /// Why not the only capture mechanism: TextPattern coverage is uneven — Java Swing, some
    /// Edge contexts, and certain custom Electron renderers either don't expose it or expose
    /// a pattern that returns empty selections even when the user clearly has text selected.
    /// Keeping Ctrl+Insert as a last-resort fallback covers those.
    /// </remarks>
    private static string? CopyViaUIA(
        uint expectedProcessId, string? expectedRuntimeId)
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element == null) return null;
            if ((uint)element.Current.ProcessId != expectedProcessId) return null;
            if (!MatchesAutomationRuntimeId(element, expectedRuntimeId)) return null;

            var walker = TreeWalker.RawViewWalker;
            for (int depth = 0; element != null && depth < TextPatternParentWalkDepth; depth++)
            {
                try
                {
                    if (element.TryGetCurrentPattern(TextPattern.Pattern, out var pat))
                    {
                        var tp = (TextPattern)pat;
                        var ranges = tp.GetSelection();
                        if (ranges != null && ranges.Length > 0)
                        {
                            // GetText(-1) returns the entire range with no length cap. For
                            // discontiguous selections (rare — Ctrl-click in Excel-style
                            // grids) join with \n so the caller sees all of it.
                            var combined = ranges.Length == 1
                                ? ranges[0].GetText(-1)
                                : string.Join("\n",
                                    ranges.Select(r => r.GetText(-1)).Where(s => !string.IsNullOrEmpty(s)));
                            if (!string.IsNullOrEmpty(combined)) return combined;
                        }
                    }
                }
                catch { /* per-level UIA failure — try the parent */ }

                try { element = walker.GetParent(element); }
                catch { break; }
            }
        }
        catch { /* UIA failure */ }
        return null;
    }

    private static bool MatchesAutomationRuntimeId(
        AutomationElement element, string? expectedRuntimeId)
    {
        if (expectedRuntimeId == null) return true;
        try
        {
            int[] runtimeId = element.GetRuntimeId();
            return runtimeId.Length > 0
                   && string.Join(",", runtimeId) == expectedRuntimeId;
        }
        catch
        {
            return false;
        }
    }

    private static bool CopyViaWindowMessage(ForegroundTarget target)
    {
        if (!target.IsComplete) return false;

        // Send only to the child HWND captured for this operation. Re-querying foreground here
        // would let an Alt-Tab redirect WM_COPY into the newly focused application.
        return SendMessageTimeout(target.FocusedWindow, WM_COPY, IntPtr.Zero, IntPtr.Zero,
            SMTO_ABORTIFHUNG, WM_COPY_TIMEOUT_MS, out _) != IntPtr.Zero;
    }

    internal static async Task<bool> PreparePasteAsync(SelectionOperation operation)
    {
        if (!await operation.CanInjectInputAsync()) return false;
        return await WaitForModifierKeysReleasedAsync(VK_SHIFT, VK_CONTROL, VK_MENU)
               && await operation.CanInjectInputAsync();
    }

    internal static async Task<bool> PrepareDeleteAsync(SelectionOperation operation)
    {
        if (!await operation.CanInjectInputAsync()) return false;
        return await WaitForModifierKeysReleasedAsync(VK_SHIFT, VK_CONTROL, VK_MENU)
               && await operation.CanInjectInputAsync();
    }

    /// <summary>
    /// Sends Shift+Insert only if the immutable operation, exact input target, physical modifiers,
    /// and optional clipboard observation all still match at the final injection boundary.
    /// Call <see cref="PreparePasteAsync"/> before changing clipboard data.
    /// </summary>
    internal static Task<InputInjectionOutcome> TrySimulatePasteAsync(
        SelectionOperation operation, ClipboardObservation? expectedClipboard = null)
    {
        return TrySendInputAsync(
            operation,
            expectedClipboard,
            ShiftInsertInputs,
            VK_SHIFT, VK_CONTROL, VK_MENU);
    }

    internal static async Task<InputInjectionOutcome> SimulatePasteAsync(
        SelectionOperation operation)
    {
        var expectedClipboard = ObserveClipboard();
        if (!await PreparePasteAsync(operation))
            return new InputInjectionOutcome(InputInjectionStatus.Rejected);
        return await TrySimulatePasteAsync(operation, expectedClipboard);
    }

    internal static async Task<InputInjectionOutcome> SimulateDeleteAsync(
        SelectionOperation operation)
    {
        if (!await PrepareDeleteAsync(operation))
            return new InputInjectionOutcome(InputInjectionStatus.Rejected);
        return await TrySendInputAsync(
            operation,
            expectedClipboard: null,
            DeleteInputs,
            VK_SHIFT, VK_CONTROL, VK_MENU);
    }

    private static async Task<InputInjectionOutcome> TrySendInputAsync(
        SelectionOperation operation,
        ClipboardObservation? expectedClipboard,
        KeyStroke[] strokes,
        params int[] modifiersThatMustBeReleased)
    {
        var outcome = new InputInjectionOutcome(
            InputInjectionStatus.Rejected);
        bool reachedInputBoundary =
            await ForegroundGuard.TryRunWithExactInputTargetAsync(
            operation.Target,
            currentTarget =>
            {
                var currentClipboard = expectedClipboard == null
                    ? default
                    : ObserveClipboard();
                if (!CanInjectAtBoundary(
                        operation.IsCurrent,
                        operation.Target,
                        currentTarget,
                        expectedClipboard,
                        currentClipboard))
                    return false;
                if (!AreModifierKeysReleased(modifiersThatMustBeReleased))
                    return false;
                // Re-sample native identity immediately before SendInput. The UIA identity was
                // captured directly before this callback on the same worker.
                if (!ForegroundGuard.StillValid(operation.Target))
                    return false;
                // Repeat the claim after every potentially yielding or cross-process validation.
                // Hook-thread invalidation remains lock-free and wins before this final send point.
                if (!TrySendKeySequenceForOperation(
                        operation,
                        strokes,
                        SendNativeKeyStrokes,
                        out outcome))
                    return false;
                return true;
            });
        return reachedInputBoundary
            ? outcome
            : new InputInjectionOutcome(InputInjectionStatus.Rejected);
    }

    private static Task<bool> TryRunClipboardMutationAsync(
        SelectionOperation operation,
        ClipboardObservation expectedClipboard,
        Func<bool> mutation)
    {
        return ForegroundGuard.TryRunWithExactInputTargetAsync(
            operation.Target,
            currentTarget =>
            {
                var currentClipboard = ObserveClipboard();
                if (!CanInjectAtBoundary(
                        operation.IsCurrent,
                        operation.Target,
                        currentTarget,
                        expectedClipboard,
                        currentClipboard))
                    return false;
                if (!ForegroundGuard.StillValid(operation.Target))
                    return false;
                if (!operation.TryClaim())
                    return false;
                return mutation();
            });
    }

    // Insert is an extended key — without the flag some apps see numpad-0 instead.
    private static KeyStroke[] BuildExtendedInsertCombo(ushort modifier) =>
    [
        new(modifier, KeyUp: false, Extended: false),
        new(VK_INSERT, KeyUp: false, Extended: true),
        new(VK_INSERT, KeyUp: true, Extended: true),
        new(modifier, KeyUp: true, Extended: false),
    ];

    internal static InputInjectionOutcome SendKeySequence(
        IReadOnlyList<KeyStroke> strokes,
        Func<IReadOnlyList<KeyStroke>, uint> sender)
    {
        // SendInput inserts an INPUT array serially and returns the inserted event count. For a
        // short prefix, synthesize key-up events for every accepted key-down not already paired
        // with an accepted key-up, in reverse press order.
        uint inserted = sender(strokes);
        if (inserted == (uint)strokes.Count)
            return new InputInjectionOutcome(
                InputInjectionStatus.Succeeded,
                AcceptedCount: inserted);
        if (inserted == 0)
            return new InputInjectionOutcome(InputInjectionStatus.Rejected);

        bool cleanupSucceeded = inserted < (uint)strokes.Count;
        if (cleanupSucceeded)
        {
            foreach (var release in BuildRecoveryKeyUps(strokes, inserted))
            {
                if (sender(new[] { release }) != 1)
                    cleanupSucceeded = false;
            }
        }

        return new InputInjectionOutcome(
            InputInjectionStatus.Partial,
            cleanupSucceeded,
            inserted);
    }

    internal static bool TrySendKeySequenceForOperation(
        SelectionOperation operation,
        IReadOnlyList<KeyStroke> strokes,
        Func<IReadOnlyList<KeyStroke>, uint> sender,
        out InputInjectionOutcome outcome)
    {
        outcome = new InputInjectionOutcome(InputInjectionStatus.Rejected);
        if (!operation.TryClaim()) return false;
        outcome = SendKeySequence(strokes, sender);
        return true;
    }

    private static IReadOnlyList<KeyStroke> BuildRecoveryKeyUps(
        IReadOnlyList<KeyStroke> strokes, uint inserted)
    {
        var pressed = new List<KeyStroke>();
        int accepted = Math.Min(strokes.Count, checked((int)inserted));
        for (int i = 0; i < accepted; i++)
        {
            var stroke = strokes[i];
            if (!stroke.KeyUp)
            {
                pressed.Add(stroke);
                continue;
            }

            int down = pressed.FindLastIndex(
                candidate => candidate.VirtualKey == stroke.VirtualKey);
            if (down >= 0) pressed.RemoveAt(down);
        }

        var releases = new List<KeyStroke>(pressed.Count);
        for (int i = pressed.Count - 1; i >= 0; i--)
        {
            var down = pressed[i];
            releases.Add(down with { KeyUp = true });
        }
        return releases;
    }

    private static uint SendNativeKeyStrokes(
        IReadOnlyList<KeyStroke> strokes)
    {
        var inputs = new INPUT[strokes.Count];
        for (int i = 0; i < strokes.Count; i++)
            inputs[i] = MakeKeyInput(strokes[i]);
        return SendInput((uint)inputs.Length, inputs, InputSize);
    }

    private static INPUT MakeKeyInput(KeyStroke stroke)
    {
        var input = new INPUT { type = INPUT_KEYBOARD };
        input.u.ki.wVk = stroke.VirtualKey;
        uint flags = 0;
        if (stroke.Extended) flags |= KEYEVENTF_EXTENDEDKEY;
        if (stroke.KeyUp) flags |= KEYEVENTF_KEYUP;
        input.u.ki.dwFlags = flags;
        return input;
    }

    // P/Invoke structs
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public int type; public InputUnion u; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT { public uint uMsg; public ushort wParamL, wParamH; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
}
