using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TeleportFX
{
    [AddComponentMenu("")]
    internal class TeleportFX_PositionCurve : TeleportFX_IScriptInstance
    {

        public AnimationCurve PositionOverLifeTime = AnimationCurve.EaseInOut(0, 0, 1, 1);
        public Vector3        Axis                 = new Vector3(0, 1, 0);
        public float          Duration             = 1;
        public bool           Loop                 = false;
        public Transform      MultiplyTransformScale;
        
        private float   _startTime;
        private Vector3 _startPosition;
        private bool    _frozen;
        private Vector3 _currentAxis;

        internal override void OnEnableExtended()
        {
            _startTime   = Time.time;
            _frozen      = false;
            _currentAxis = Axis;

            if (MultiplyTransformScale != null) _currentAxis = Vector3.Scale(_currentAxis, MultiplyTransformScale.lossyScale);

             _startPosition    = transform.position;
            transform.position = PositionOverLifeTime.Evaluate(0) * _currentAxis + _startPosition;
        }


        internal override void OnDisableExtended()
        {
            transform.position = _startPosition;
        }

        internal override void ManualUpdate()
        {
            if (_frozen) return;
            
            var leftTime       = Time.time - _startTime;
            if (Loop) leftTime %= Duration;
            var sizeValue      = PositionOverLifeTime.Evaluate(leftTime / Duration) * _currentAxis + _startPosition;
            transform.position = sizeValue;

            if (!Loop && leftTime > Duration) _frozen = true;
        }

    }
}