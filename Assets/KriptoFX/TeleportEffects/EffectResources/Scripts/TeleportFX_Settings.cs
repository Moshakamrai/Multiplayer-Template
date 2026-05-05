using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TeleportFX
{
    [AddComponentMenu("")]
    internal class TeleportFX_Settings : MonoBehaviour
    {
        public GameObject[] LightObjects;

        [Space]
        public Shader     Shader;
        
        [Space] 
        public bool           UseVertexTeleportation  = false;
        public         float          VertexTeleportationTime = 1;
        public         AnimationCurve VertexTeleporationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
      
        [Space]
        public bool UseDissolveByTime = false;
        public float CutoutTime = 1;
        public AnimationCurve CutoutCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Space]
        public bool UseDissolveByHeight = false;
        public Transform DissolveAnchor;
        public float     DissolveByHeightDuration = 2;

        [Space] 
        public bool      UseVertexPositionAsUV;
        public bool      OverrideTexture;
        public Texture2D NoiseTexture;
        public Vector2   NoiseStrength = new Vector2(1.0f, 0.0f);
        public Vector2   NoiseScale    = Vector2.one;

        [ColorUsage(true, true)] public Color   DissolveColor1   = Color.yellow;
        [ColorUsage(true, true)] public Color   DissolveColor2   = Color.red;
        [ColorUsage(true, true)] public Color   DissolveColor3   = Color.black;
        public                          Vector3 DissolveThresold = new Vector3(0.75f, 0.75f, 0.9f);
        
    }
}
