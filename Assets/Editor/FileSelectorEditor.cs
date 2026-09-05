using UnityEditor;
using UnityEngine;
using UnityEditor.UI;


[CustomEditor(typeof(FileSelector), false)]
[CanEditMultipleObjects]
public class FileSelectorEditor : InputFieldEditor {
    private SerializedProperty fileBrowser;
    private SerializedProperty browseButton;
    private SerializedProperty onPathSelected;

    protected override void OnEnable() {
        base.OnEnable();

        fileBrowser = serializedObject.FindProperty("fb");
        browseButton = serializedObject.FindProperty("browseBtn");
        onPathSelected = serializedObject.FindProperty("OnPathSelected");
    }

    public override void OnInspectorGUI() {
        serializedObject.Update();

        EditorGUILayout.PropertyField(fileBrowser, new GUIContent("File Browser"));
        EditorGUILayout.PropertyField(browseButton, new GUIContent("Browse Button"));
        EditorGUILayout.PropertyField(onPathSelected);

        serializedObject.ApplyModifiedProperties();

        base.OnInspectorGUI();
    }
}
