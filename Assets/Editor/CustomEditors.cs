using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class CustomEditors
{
    [CustomEditor(typeof(ControlParameter))]
    public class ControlParameterUIEditor : Editor
    {
        // This object reference is stored in the editor only
        private ControlSO editorOnlyControlSO;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Draw the default inspector
            DrawDefaultInspector();
        
            // Create an editor-only field for the Control SO
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Editor Tools", EditorStyles.boldLabel);
        
            // Object field that doesn't rely on a serialized property
            editorOnlyControlSO = (ControlSO)EditorGUILayout.ObjectField(
                "Control SO", editorOnlyControlSO, typeof(ControlSO), false);

            if (editorOnlyControlSO != null)
            {
                ControlParameter controlParameter = (ControlParameter)target;
                
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
