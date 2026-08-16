using Brawl;
using UnityEditor;
using UnityEngine;

namespace Brawl.EditorTools
{
    [CustomEditor(typeof(BrawlAirWall))]
    public sealed class BrawlAirWallEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(
                "默认跟场景里摆的走：拖父物体改位置/缩放，或单独拖四堵墙。运行时不会改大小。\n需要按 InnerSize 重排时，点下面按钮，或勾选「Lock Walls To Size」。",
                MessageType.Info);

            var wall = (BrawlAirWall)target;
            if (GUILayout.Button("按尺寸重排墙体"))
            {
                Undo.RecordObject(wall.transform, "Apply AirWall Layout");
                foreach (Transform child in wall.transform)
                    Undo.RecordObject(child, "Apply AirWall Layout");
                wall.ApplyLayout();
                EditorUtility.SetDirty(wall);
            }
        }
    }
}
