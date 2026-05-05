using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TeleportFX
{
    public abstract class TeleportFX_IScriptInstance : MonoBehaviour
    {
        protected virtual void OnEnable()
        {
            TeleportFX_GlobalUpdate.CreateInstanceIfRequired();
            TeleportFX_GlobalUpdate.ScriptInstances.Add(this);
            OnEnableExtended();
        }

        protected virtual void OnDisable()
        {
            TeleportFX_GlobalUpdate.ScriptInstances.Remove(this);
            OnDisableExtended();
        }

        internal abstract void OnEnableExtended();
        internal abstract void OnDisableExtended();

        internal abstract void ManualUpdate();
    }
}