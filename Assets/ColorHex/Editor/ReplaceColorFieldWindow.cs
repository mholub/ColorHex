using UnityEngine;
using UnityEditor;
using System.Collections;

public class ReplaceColorFieldWindow : EditorWindow {
	[MenuItem("Window/Hex ColorPicker Hack")]
	public static void OpenWindow() {
		EditorWindow.GetWindow<ReplaceColorFieldWindow>().Show();
	}

	void OnGUI() {

		GUILayout.BeginVertical();
		GUILayout.Space(10);
		GUILayout.Label("It will inject custom code into UnityEditor.dll.\nDo it on you own risk.");
		GUILayout.FlexibleSpace();
		if (!ReplaceColorField.CheckIfPatchedAlready()) {
			if (GUILayout.Button("Patch UnityEditor.dll")) {
				string backupPath = EditorUtility.SaveFilePanel("Backup UnityEditor.dll", null, "UnityEditor", "dll");
				if (backupPath != null && backupPath.Length > 0) {
					ReplaceColorField.Patch(backupPath);
				}
			}
		} else {
			if (GUILayout.Button("Restore original UnityEditor.dll")) {
				string backupPath = EditorUtility.OpenFilePanel("Backup UnityEditor.dll", "UnityEditor", "dll");
				ReplaceColorField.Restore(backupPath);
			}
		}
		GUILayout.Space(10);
		GUILayout.EndVertical();
	}
}
