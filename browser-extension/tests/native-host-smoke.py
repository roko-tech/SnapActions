"""Exercise the published Windows host's real stdio/pipe relay without a browser.

Uses a separate data directory and pipe, leaving the desktop app running.
"""
import argparse
import csv
import ctypes
from ctypes import wintypes
import io
import json
import os
import hashlib
import tempfile
import struct
import subprocess
import threading
import time


def run_case(executable, connect_delay, close_stdin=True):
    sid = next(csv.reader(io.StringIO(subprocess.check_output(
        ["whoami", "/user", "/fo", "csv", "/nh"], text=True))))[1]
    isolated = tempfile.TemporaryDirectory(prefix="snapactions-native-")
    data_dir = str(__import__("pathlib").Path(isolated.name).resolve())
    suffix = hashlib.sha256(data_dir.upper().encode("utf-8")).hexdigest()[:16].upper()
    environment = os.environ.copy()
    environment["SNAPACTIONS_DATA_DIR"] = data_dir
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.CreateNamedPipeW.restype = wintypes.HANDLE
    kernel.CreateNamedPipeW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD,
                                       wintypes.DWORD, wintypes.DWORD, wintypes.DWORD,
                                       wintypes.DWORD, wintypes.LPVOID]
    kernel.ConnectNamedPipe.argtypes = [wintypes.HANDLE, wintypes.LPVOID]
    kernel.ReadFile.argtypes = [wintypes.HANDLE, wintypes.LPVOID, wintypes.DWORD,
                               ctypes.POINTER(wintypes.DWORD), wintypes.LPVOID]
    kernel.WriteFile.argtypes = kernel.ReadFile.argtypes
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    errors = []
    text = "استخدم ChatGPT (42)!\nEnglish مع العربية 👋"
    payload = json.dumps({"id": 123, "text": text}, ensure_ascii=False).encode("utf-8")
    framed = struct.pack("<I", len(payload)) + payload

    def serve():
        time.sleep(connect_delay)
        # FIRST_PIPE_INSTANCE prevents this test from intercepting an existing desktop bridge.
        pipe = kernel.CreateNamedPipeW(fr"\\.\pipe\SnapActions.Browser.{sid}.{suffix}", 3 | 0x00080000,
                                      0, 1, 262144, 262144, 0, None)
        if pipe == wintypes.HANDLE(-1).value:
            raise ctypes.WinError(ctypes.get_last_error())
        try:
            if not kernel.ConnectNamedPipe(pipe, None) and ctypes.get_last_error() != 535:
                raise ctypes.WinError(ctypes.get_last_error())
            for chunk in [framed[:2], framed[2:7], framed[7:]]:
                count = wintypes.DWORD()
                if not kernel.WriteFile(pipe, chunk, len(chunk), ctypes.byref(count), None):
                    raise ctypes.WinError(ctypes.get_last_error())
            received = bytearray()
            while len(received) < len(framed):
                buffer = ctypes.create_string_buffer(len(framed) - len(received))
                count = wintypes.DWORD()
                if not kernel.ReadFile(pipe, buffer, len(buffer), ctypes.byref(count), None):
                    raise ctypes.WinError(ctypes.get_last_error())
                received.extend(buffer.raw[:count.value])
            assert bytes(received) == framed, "Native host changed the UTF-8 message"
        except BaseException as error:
            errors.append(error)
        finally:
            kernel.CloseHandle(pipe)

    worker = threading.Thread(target=serve, daemon=True)
    worker.start()
    process = subprocess.Popen([executable, "chrome-extension://ccgckebadlhbplacbcjbfohpinbdoehh/"],
                               stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                               creationflags=subprocess.CREATE_NO_WINDOW, env=environment)
    try:
        process.stdin.write(framed)
        process.stdin.flush()
        output_parts = []
        def read_output():
            while sum(map(len, output_parts)) < len(framed):
                chunk = process.stdout.read(len(framed) - sum(map(len, output_parts)))
                if not chunk: break
                output_parts.append(chunk)
        reader = threading.Thread(target=read_output, daemon=True)
        reader.start()
        reader.join(12)
        assert not reader.is_alive(), "Native host response timed out"
        output = b"".join(output_parts)
        if close_stdin:
            process.stdin.close()
        process.wait(timeout=5)
        errors_output = process.stderr.read()
        worker.join(2)
        assert not worker.is_alive(), "Host did not connect to the pipe"
        if errors:
            raise errors[0]
        assert process.returncode == 0, errors_output.decode(errors="replace")
        assert output == framed, f"Unexpected native stdout: {output!r}"
        print(f"PASS: native host UTF-8 round trip and clean exit (connection delay {connect_delay}s, browser stdin {'closed' if close_stdin else 'left open'})")
    finally:
        if process.poll() is None:
            process.kill()
            process.wait()
        process.stdin.close()
        process.stdout.close()
        process.stderr.close()
        isolated.cleanup()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("executable")
    args = parser.parse_args()
    run_case(args.executable, 0)
    run_case(args.executable, 0.4)
    # The browser keeps its stdio port open when the desktop app exits/restarts.
    # The host must exit on pipe EOF so the companion can start a fresh connection.
    run_case(args.executable, 0, close_stdin=False)


if __name__ == "__main__":
    main()
