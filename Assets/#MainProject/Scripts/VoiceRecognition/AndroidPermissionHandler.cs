using UnityEngine;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class AndroidPermissionHandler : MonoBehaviour
{
    private const string MICROPHONE_PERMISSION = "android.permission.RECORD_AUDIO";

    public static void RequestMicrophonePermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(MICROPHONE_PERMISSION))
        {
            Permission.RequestUserPermission(MICROPHONE_PERMISSION);
        }
#endif
    }

    public static bool HasMicrophonePermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return Permission.HasUserAuthorizedPermission(MICROPHONE_PERMISSION);
#else
        return true;
#endif
    }
}
