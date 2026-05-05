using System;
using System.Collections;
using System.Collections.Generic;
using TeleportFX;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace MagicFX5
{
    [AddComponentMenu("")]
    public class TeleportFX_GUI : MonoBehaviour
    {
        public GameObject[] Effects;
        public float        DelayToDisable = 1;

        private GameObject             _effectInstance;
        private int                    _currentEffectIndex;
        private KriptoFX_Teleportation _teleportScript;

        void OnEnable()
        {
            UpdateEffect();
        }

        void OnDisable()
        {
            CancelInvoke("LateDisable");
        }

        void OnGUI()
        {
            if (GUI.Button(new Rect(30,  30, 120, 30), "Previous")) PreviousEffect();
            if (GUI.Button(new Rect(170, 30, 120, 30), "Next")) NextEffect();
        }

        public void NextEffect()
        {
            if (++_currentEffectIndex >= Effects.Length) _currentEffectIndex = 0;
            UpdateEffect();
        }

        public void PreviousEffect()
        {
            if (--_currentEffectIndex < 0) _currentEffectIndex = Effects.Length - 1;
            UpdateEffect();
        }

        void UpdateEffect()
        {
            if (_effectInstance != null)
            {
                CancelInvoke("LateDisable");
                _teleportScript.IsTeleportationFinished -= IsTeleportationFinished;
                Destroy(_effectInstance);
            }

            _effectInstance = Instantiate(Effects[_currentEffectIndex], transform);
            _teleportScript = _effectInstance.GetComponent<KriptoFX_Teleportation>();
            _teleportScript.IsTeleportationFinished += IsTeleportationFinished;
        }

        private void IsTeleportationFinished()
        {
            Invoke("LateDisable", DelayToDisable);
        }


        void LateDisable()
        {
            _teleportScript.TeleportationState = KriptoFX_Teleportation.TeleportationStateEnum.Disappear;
        }
    }
}