using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class MicTest : MonoBehaviour
{
    [Header("LEGACY UI")]
    public Text statusText;

    [Header("Mic Settings")]
    public int sampleRate = 16000;

    private string micName;

    void Start()
    {
        StartCoroutine(CheckMicRoutine());
    }

    IEnumerator CheckMicRoutine()
    {
        statusText.text = "Checking microphone...";

        // IMPORTANT: delay for builds
        yield return new WaitForSeconds(0.5f);

        // 1️⃣ Check mic devices
        if (Microphone.devices.Length == 0)
        {
            statusText.text =
                "❌ NO MICROPHONE FOUND\n\n" +
                "Check Windows:\n" +
                "Settings → Privacy → Microphone\n" +
                "Enable Desktop App Access";
            yield break;
        }

        micName = Microphone.devices[0];
        statusText.text = "🎤 Mic found:\n" + micName;

        // 2️⃣ Try to start mic
        AudioClip clip = Microphone.Start(micName, true, 5, sampleRate);

        yield return new WaitForSeconds(0.2f);

        // 3️⃣ Verify mic actually started
        if (!Microphone.IsRecording(micName))
        {
            statusText.text =
                "❌ MIC FOUND BUT NOT RECORDING\n\n" +
                "Possible reasons:\n" +
                "- Started twice in code\n" +
                "- OS blocked mic\n" +
                "- Mic already in use";
            yield break;
        }

        // 4️⃣ Success
        statusText.text = "✅ MICROPHONE ACTIVE\nListening...";
    }
}
