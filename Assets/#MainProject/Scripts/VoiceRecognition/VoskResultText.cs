using UnityEngine;
using UnityEngine.UI;

public class VoskResultText : MonoBehaviour 
{
    public VoskSpeechToText VoskSpeechToText;
    public Text ResultText;

    void Awake()
    {
        VoskSpeechToText.OnTranscriptionResult += OnTranscriptionResult;
    }

    private void OnTranscriptionResult(string obj)
    {
        // DEBUG UI DISABLED — recognition still works, just no text output
        // Debug.Log(obj);
    }
}
