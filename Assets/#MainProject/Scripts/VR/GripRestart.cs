using UnityEngine;
using UnityEngine.XR;
using UnityEngine.SceneManagement;
using Mirror;

/// Hold BOTH grip buttons for 3 seconds to restart the whole game (reloads the active scene, which
/// fully resets the match — inventory, rounds, scores). Self-bootstraps; nothing to set up in a scene.
/// An emergency/event reset so a booth attendant can wipe a stuck or finished session instantly.
[DefaultExecutionOrder(10010)]
public class GripRestart : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("~GripRestart");
        DontDestroyOnLoad(go);
        go.AddComponent<GripRestart>();
    }

    private const float HOLD_SECONDS = 3f;
    private const float GRIP_THRESHOLD = 0.6f;   // analog grip press point
    private float _heldFor;
    private bool _restarting;

    private void Update()
    {
        if (_restarting) return;

        bool bothGrips = GripDown(XRNode.LeftHand) && GripDown(XRNode.RightHand);
        if (bothGrips)
        {
            _heldFor += Time.unscaledDeltaTime;
            if (_heldFor >= HOLD_SECONDS) Restart();
        }
        else
        {
            _heldFor = 0f; // released → reset the timer
        }
    }

    private static bool GripDown(XRNode node)
    {
        InputDevice d = InputDevices.GetDeviceAtXRNode(node);
        if (!d.isValid) return false;
        // Prefer the analog grip axis; fall back to the boolean grip button.
        if (d.TryGetFeatureValue(CommonUsages.grip, out float g)) return g >= GRIP_THRESHOLD;
        if (d.TryGetFeatureValue(CommonUsages.gripButton, out bool b)) return b;
        return false;
    }

    private void Restart()
    {
        _restarting = true;
        Debug.Log("[GripRestart] Both grips held 3s → restarting game.");

        string scene = SceneManager.GetActiveScene().name;

        // Networked restart when we're the host/server (matches how match-end resets); otherwise just
        // reload the scene locally. Stop networking first so a fresh session starts clean.
        if (NetworkServer.active && NetworkManager.singleton != null)
        {
            NetworkManager.singleton.ServerChangeScene(scene);
        }
        else
        {
            if (NetworkManager.singleton != null)
            {
                if (NetworkClient.isConnected) NetworkManager.singleton.StopClient();
                NetworkManager.singleton.StopHost();
            }
            Time.timeScale = 1f; // in case a slow-mo was mid-effect
            SceneManager.LoadScene(scene);
        }
    }
}
