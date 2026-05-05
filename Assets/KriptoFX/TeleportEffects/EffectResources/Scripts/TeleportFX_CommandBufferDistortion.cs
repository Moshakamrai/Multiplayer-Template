using UnityEngine;


namespace TeleportFX
{
    [ExecuteAlways]
    [AddComponentMenu("")]
    public class TeleportFX_CommandBufferDistortion : MonoBehaviour
    {

        void OnEnable()
        {
            TeleportFX_GlobalUpdate.CreateInstanceIfRequired();
            TeleportFX_GlobalUpdate.DistortionInstances.Add(this);

        }

        void OnDisable()
        {
            TeleportFX_GlobalUpdate.DistortionInstances.Remove(this);
        }
    }
}