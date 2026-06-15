using System;
using System.IO;
using UnityEngine;

/// One reliable "pick an audio file" dialog shared by both beat mappers (Smart + manual). The old
/// per-mapper version shelled out to PowerShell with -NonInteractive and CreateNoWindow, which often
/// failed silently — the dialog never appeared (or popped behind Unity's window) and the Browse
/// button "did nothing". This:
///   • Uses Unity's native EditorUtility.OpenFilePanel in the Editor (instant, reliable).
///   • In a standalone Windows build, runs PowerShell WITHOUT -NonInteractive and with -STA so the
///     WinForms dialog actually shows, and surfaces any error instead of swallowing it.
///
/// Result is delivered on the main thread via a callback queued through the returned poll object —
/// callers check ResultReady each frame (GUI code can't touch the picked path from a worker thread).
public static class NativeFileDialog
{
    /// A pending pick. Poll it from OnGUI/Update: when Done is true, Path holds the chosen file
    /// ("" if cancelled) and Error holds any failure message.
    public class Pick
    {
        public volatile bool   Done;
        public volatile string Path = "";
        public volatile string Error = "";
    }

    /// Open an audio-file picker. Returns a Pick to poll, or null in the Editor where the result is
    /// resolved synchronously (use the out param there).
    public static Pick OpenAudio(out string editorImmediateResult)
    {
        editorImmediateResult = null;

#if UNITY_EDITOR
        // Native Unity panel — synchronous, always works, no shelling out.
        editorImmediateResult = UnityEditor.EditorUtility.OpenFilePanel(
            "Select Audio File", "", "mp3,wav,ogg,aiff,aif");
        return null;
#elif UNITY_STANDALONE_WIN
        var pick = new Pick();
        var t = new System.Threading.Thread(() =>
        {
            try
            {
                // NOTE: no -NonInteractive (it suppresses the GUI dialog); -STA is required for WinForms
                // file dialogs; we DO want a window so the dialog can grab focus.
                const string ps =
                    "Add-Type -AssemblyName System.Windows.Forms; " +
                    "$d = New-Object System.Windows.Forms.OpenFileDialog; " +
                    "$d.Title = 'Select Audio File'; " +
                    "$d.Filter = 'Audio Files|*.mp3;*.wav;*.ogg;*.aiff;*.aif|All Files|*.*'; " +
                    "$d.Multiselect = $false; " +
                    "if ($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { Write-Output $d.FileName }";

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName               = "powershell.exe",
                    Arguments              = $"-NoProfile -STA -Command \"{ps}\"",
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true
                };

                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) { pick.Error = "Could not start the file dialog (powershell not found)."; pick.Done = true; return; }

                string outp = proc.StandardOutput.ReadToEnd().Trim();
                string err  = proc.StandardError.ReadToEnd().Trim();
                proc.WaitForExit();

                if (!string.IsNullOrEmpty(outp))      pick.Path  = outp;
                else if (!string.IsNullOrEmpty(err))   pick.Error = err;   // cancelled = both empty → no error
            }
            catch (Exception e)
            {
                pick.Error = e.Message;
            }
            finally
            {
                pick.Done = true;
            }
        });
        t.IsBackground = true;
        t.Start();
        return pick;
#else
        var unsupported = new Pick { Done = true, Error = "Browse not supported on this platform — paste the path manually." };
        return unsupported;
#endif
    }
}
