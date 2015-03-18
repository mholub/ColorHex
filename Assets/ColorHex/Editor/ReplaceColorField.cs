using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.IO;

[InitializeOnLoad]
public static class ReplaceColorField
{
	static Assembly editorAssembly;
	static PropertyInfo colorProp;
	static string unityEditorDLLPath;
	static string mainDLLPath;
	static AssemblyDefinition editorAssemblyDef;
	static AssemblyDefinition mainAssemblyDef;

	static ReplaceColorField ()
	{
		foreach (var ass in AppDomain.CurrentDomain.GetAssemblies()) {
			if (ass.FullName.Contains ("UnityEditor,")) {
				unityEditorDLLPath = ass.Location;
				editorAssembly = ass;
				colorProp = editorAssembly.GetType ("UnityEditor.ColorPicker").GetProperty ("color", BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy);
				editorAssemblyDef = AssemblyDefinition.ReadAssembly (unityEditorDLLPath);
			} else {
				if (ass.GetType ("ReplaceColorField") != null) {
					mainDLLPath = ass.Location;
					mainAssemblyDef = AssemblyDefinition.ReadAssembly (mainDLLPath);
				}	
			}
		}
	}

	public static bool CheckIfPatchedAlready ()
	{
		bool foundPatchedType = false;
		foreach (var t in editorAssemblyDef.MainModule.Types) {
			if (t.Name == "ReplaceColorField") {
				foundPatchedType = true;
				break;
			}
		}
		return foundPatchedType;
	}
  
	public static void Patch (string backupPath)
	{
		if (unityEditorDLLPath != null && mainDLLPath != null) {
			if (CheckIfPatchedAlready ()) {			    
				Debug.Log ("No need to patch");
				return; // don't need to patch
			} else {
				saveOriginalDLL(backupPath);
		
				Debug.Log ("No type in editor. Copying...");

				TypeDefinition typeToCopy = null;
				foreach (var t in mainAssemblyDef.MainModule.Types) {
					if (t.Name == "ReplaceColorField") {
						typeToCopy = t;
						break;
					}
				}
				if (typeToCopy == null) {
					Debug.LogError ("ReplaceColorField is not present in main assembly");
				} else {
					var copiedType = copyTypeToAssembly (typeToCopy, editorAssemblyDef);
					var onGUIFooterMethodImported = copiedType.Methods.First (m => m.Name == "OnGUIFooter");

					var colorPickerType = editorAssemblyDef.MainModule.Types.First (t => t.Name == "ColorPicker");
					var onGUIMethod = colorPickerType.Methods.First (m => m.Name == "OnGUI");
					var colorSlidersInst = onGUIMethod.Body.Instructions.Last (i => 
						                                                          i.OpCode == OpCodes.Call && 
						(i.Operand as MethodDefinition) != null && 
						(i.Operand as MethodDefinition).Name.Contains ("DoColorSliders"));			
					Instruction callOnGUIFooter = Instruction.Create (OpCodes.Call, onGUIFooterMethodImported);
					onGUIMethod.Body.GetILProcessor ().InsertAfter (colorSlidersInst, callOnGUIFooter);

					editorAssemblyDef.Write (unityEditorDLLPath.Replace ("UnityEditor212312", "UnityEditorTest"), new WriterParameters { WriteSymbols = true});
					Debug.LogFormat ("ReplaceColorField: {0} was patched", unityEditorDLLPath);
					Debug.LogFormat ("Restart Unity to see the effect");
				}
			}
		}
	}

	static TypeDefinition copyTypeToAssembly (TypeDefinition type, AssemblyDefinition assDef)
	{

		var copiedType = new TypeDefinition (type.Namespace, type.Name, type.Attributes, assDef.MainModule.Import (type.BaseType));
		assDef.MainModule.Types.Add (copiedType);

		foreach (FieldDefinition field in type.Fields) {
			createFieldInType (field, assDef, copiedType);
		}

		foreach (MethodDefinition method in type.Methods) {
			createMethodInType (method, assDef, copiedType);
		}

		foreach (MethodDefinition method in type.Methods) {
			copyMethodBodyToType (method, assDef, copiedType);
		}

		return copiedType;
	}

	static FieldDefinition createFieldInType (FieldDefinition field, AssemblyDefinition assDef, TypeDefinition type)
	{
		var fieldCopy = new FieldDefinition (field.Name, field.Attributes, 
		                                    assDef.MainModule.Import (field.FieldType));
		type.Fields.Add (fieldCopy);
		return fieldCopy;
	}

	static MethodDefinition createMethodInType (MethodDefinition method, AssemblyDefinition assDef, TypeDefinition type)
	{
		var methodCopy = new MethodDefinition (
			method.Name,
			method.Attributes,
			assDef.MainModule.Import (method.ReturnType));
		foreach (var param in method.Parameters) {
			var importedParam = new ParameterDefinition (param.Name, param.Attributes, assDef.MainModule.Import (param.ParameterType));
			methodCopy.Parameters.Add (importedParam);

		}
		foreach (var varDef in method.Body.Variables) {
			methodCopy.Body.Variables.Add (new VariableDefinition (varDef.Name, 
			                                                     assDef.MainModule.Import (varDef.VariableType)));
		}

		methodCopy.CallingConvention = method.CallingConvention;
		methodCopy.SemanticsAttributes = method.SemanticsAttributes;

		type.Methods.Add (methodCopy);
		Debug.LogFormat ("METHOD_ADD: {0} to {1}", method, type);
		return methodCopy;
	}
	// implementation is not generic at all, it works only with my code
	static MethodDefinition copyMethodBodyToType (MethodDefinition method, AssemblyDefinition assDef, TypeDefinition type)
	{
		var rawInstructionConstructor = typeof(Instruction).GetConstructor (BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] {
			typeof(OpCode),
			typeof(object)
		}, null);

		MethodDefinition methodCopy = null;
		for (int z = 0; z < type.Methods.Count; z++) {
			Debug.Log ("FF: " + type.Methods [z].FullName);
			if (method.FullName == type.Methods [z].FullName) {
				methodCopy = type.Methods [z];
				break;
			}
		}

		if (methodCopy == null) {
			Debug.LogError ("No method body found: " + method);
			return null;
		}

		var copiedIL = methodCopy.Body.GetILProcessor ();
		
		List<OpCode> passThroughOpcodes = new List<OpCode> (){
			OpCodes.Ldstr, OpCodes.Br,
			OpCodes.Brfalse, OpCodes.Blt,
			OpCodes.Ldloca_S, OpCodes.Brtrue, 
			OpCodes.Leave, OpCodes.Ldc_I4_S, 
			OpCodes.Blt_S, OpCodes.Brtrue_S, OpCodes.Stloc_S, OpCodes.Ldloc_S,
			OpCodes.Bne_Un, OpCodes.Ldc_R4, OpCodes.Ldarga_S, OpCodes.Starg_S,
			OpCodes.Ldc_I4, OpCodes.Br_S
		};
		
		List<OpCode> fieldOpCodes = new List<OpCode> (){
			OpCodes.Stsfld, OpCodes.Ldsfld, OpCodes.Ldfld, OpCodes.Stfld,
			OpCodes.Ldflda
			
		};
		
		List<OpCode> methodOpCodes = new List<OpCode> (){
			OpCodes.Call, OpCodes.Callvirt, OpCodes.Ldftn, OpCodes.Newobj
			
		};
		
		List<OpCode> typeOpCodes = new List<OpCode> (){
			OpCodes.Newarr, OpCodes.Ldtoken, OpCodes.Castclass, OpCodes.Isinst, 
			OpCodes.Unbox_Any, OpCodes.Box
			
		};
		
		for (int k = 0; k < method.Body.Instructions.Count; k++) {
			var i = method.Body.Instructions [k];
			Instruction importedI = (Instruction)rawInstructionConstructor.Invoke (new object[] {
				i.OpCode,
				i.Operand
			});
			importedI.Offset = i.Offset;
			
			if (passThroughOpcodes.Contains (i.OpCode)) {
				importedI.Operand = i.Operand; 
			} else if (methodOpCodes.Contains (i.OpCode)) {
				Debug.Log ("CALL: " + i + " " + i.Operand + " " + i.Operand.GetType ());
				var methodReference = i.Operand as MethodReference;
				if (methodReference.DeclaringType == method.DeclaringType) {
					MethodDefinition localMethod = null;

					for (int j = 0; j < type.Methods.Count; j++) {
						//				Debug.Log("FULL NAME: " + type.Methods[j].FullName);
						if (methodReference.FullName == type.Methods [j].FullName) {
							localMethod = type.Methods [j];
							break;
						}
					}
					if (localMethod == null) {
						Debug.LogErrorFormat ("TYPE: {0} METHODS COUNT: {1}", type, type.Methods.Count);
						Debug.LogErrorFormat ("Not found: {0} for {1}", methodReference, method);
					}
					importedI.Operand = localMethod;
				} else {
					importedI.Operand = editorAssemblyDef.MainModule.Import (methodReference);
				}
				
			} else if (fieldOpCodes.Contains (i.OpCode)) {
				Debug.Log ("FIELD: " + i + " " + i.Operand + " " + i.Operand.GetType ());
				var fieldDefinition = i.Operand as FieldDefinition;
				if (fieldDefinition != null) {
					if (fieldDefinition.DeclaringType == method.DeclaringType) {
						FieldDefinition localField = null;
						for (int j = 0; j < type.Fields.Count; j++) {
//							Debug.Log("FULL NAME: " + type.Fields[j].FullName);
							if (fieldDefinition.FullName == type.Fields [j].FullName) {
								localField = type.Fields [j];
								break;
							}
						}
						importedI.Operand = localField;
					} else {
						importedI.Operand = editorAssemblyDef.MainModule.Import (fieldDefinition);
					}
				} else {
					var fieldReference = i.Operand as FieldReference;
					importedI.Operand = editorAssemblyDef.MainModule.Import (fieldReference);
				}
				
			} else if (typeOpCodes.Contains (i.OpCode)) {
				Debug.Log ("TYPE: " + i + " " + i.Operand + " " + i.Operand.GetType ());
				var typeReference = i.Operand as TypeReference;
				if (typeReference != null) {
					importedI.Operand = editorAssemblyDef.MainModule.Import (typeReference);
				} else {
					var typeDefinition = i.Operand as TypeDefinition;
					importedI.Operand = editorAssemblyDef.MainModule.Import (typeDefinition);
				}            
			} else if (i.Operand != null) {
				Debug.LogError ("Unknown: " + i + " " + i.Operand);
				if (i.Operand != null) {
					Debug.Log (i.Operand.GetType ());
				}
				throw new Exception ("Unknown OpCode: " + i.OpCode);
			}
			copiedIL.Append (importedI);
		}

		Debug.Log ("BODY: " + method);
		for (int h = 0; h < method.Body.Instructions.Count; h++) {
			Debug.Log ("Method: " + h + " " + method.Body.Instructions [h]);
			Debug.Log ("MethodCopy: " + h + " " + methodCopy.Body.Instructions [h]);
		}
    
		return methodCopy;
	}
  
	public static void Restore (string backupPath)
	{
		File.Copy (backupPath, unityEditorDLLPath, true);
		//FileUtil.MoveFileOrDirectory(backupPath, unityEditorDLLPath);
		Debug.LogFormat ("Reopen Unity to load original UnityEditor.dll");
	}

	static void saveOriginalDLL (string backupPath)
	{
		File.Copy (unityEditorDLLPath, backupPath, true);
		Debug.LogFormat ("Original UnityEditor.dll stored as {0}", backupPath);
	}

	public static void OnGUIFooter ()
	{

		string t = Application.dataPath;

		//Color c0 = (Color)colorProp.GetValue(null, null);

		GUILayout.Space (10f);

		Color c0 = (Color)colorProp.GetValue (null, null);

		var hex = EditorGUILayout.TextField ("#" + ColorToHex (c0).ToLower ());
		Color c = HexToColor (hex);

		if (c != Color.clear) {
			c.a = c0.a;
			colorProp.SetValue (null, c, null);
		}

	}

	// Note that Color32 and Color implictly convert to each other. You may pass a Color object to this method without first casting it.
	static string ColorToHex (Color32 color)
	{
		string hex = color.r.ToString ("X2") + color.g.ToString ("X2") + color.b.ToString ("X2");
		return hex;
	}
	
	static Color HexToColor (string hex)
	{
		hex = hex.Replace ("#", "");
		try {
			if (hex.Length == 6) {
				byte r = byte.Parse (hex.Substring (0, 2), System.Globalization.NumberStyles.HexNumber);
				byte g = byte.Parse (hex.Substring (2, 2), System.Globalization.NumberStyles.HexNumber);
				byte b = byte.Parse (hex.Substring (4, 2), System.Globalization.NumberStyles.HexNumber);
				return new Color32 (r, g, b, 255);
			} else {
				return Color.clear;
			}
		} catch (Exception e) {
			return Color.clear;
		}
	}

	static void TestMethod ()
	{
		Debug.Log ("TEST TEST TEST");
	}
}
