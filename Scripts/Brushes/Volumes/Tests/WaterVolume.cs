﻿#if UNITY_EDITOR || RUNTIME_CSG

using Sabresaurus.SabreCSG;
using System;
using UnityEngine;

namespace DeleteMeBeforePublishing
{
    /// <summary>
    /// Simple water volume example for Kerfuffles.
    /// </summary>
    /// <seealso cref="Sabresaurus.SabreCSG.Volume"/>
    [Serializable]
    public class WaterVolume : Volume
    {
        [SerializeField]
        public int thickness = 0;

#if UNITY_EDITOR

        /// <summary>
        /// Called when the inspector GUI is drawn in the editor.
        /// </summary>
        /// <returns>True if a property changed or else false.</returns>
        public override bool OnInspectorGUI()
        {
            UnityEditor.EditorGUILayout.LabelField("Water Volume");

            int previousThickness = thickness;
            thickness = UnityEditor.EditorGUILayout.IntField("Thickness", thickness);
            if (thickness != previousThickness)
                return true; // true when a property changed, the brush invalidates and stores all changes.
            return false;
        }

#endif

        /// <summary>
        /// Called when the volume is created in the editor.
        /// </summary>
        /// <param name="volume">The generated volume game object.</param>
        public override void OnCreateVolume(GameObject volume)
        {
            WaterVolumeComponent component = volume.AddComponent<WaterVolumeComponent>();
            component.thickness = thickness;
        }
    }
}

#endif