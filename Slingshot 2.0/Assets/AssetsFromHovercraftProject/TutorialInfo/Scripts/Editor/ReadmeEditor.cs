using System.IO;
using UnityEditor;
using UnityEngine;

namespace HovercraftProject.TutorialInfo
{
    [CustomEditor(typeof(Readme))]
    public class ReadmeEditor : Editor
    {
        static string s_ReadmeSourceDirectory = "Assets/AssetsFromHovercraftProject/TutorialInfo";

        const float k_Space = 16f;

        bool m_Initialized;

        [SerializeField]
        GUIStyle m_LinkStyle;

        [SerializeField]
        GUIStyle m_TitleStyle;

        [SerializeField]
        GUIStyle m_HeadingStyle;

        [SerializeField]
        GUIStyle m_BodyStyle;

        [SerializeField]
        GUIStyle m_ButtonStyle;

        GUIStyle LinkStyle => m_LinkStyle;
        GUIStyle TitleStyle => m_TitleStyle;
        GUIStyle HeadingStyle => m_HeadingStyle;
        GUIStyle BodyStyle => m_BodyStyle;
        GUIStyle ButtonStyle => m_ButtonStyle;

        static void RemoveTutorial()
        {
            if (!EditorUtility.DisplayDialog(
                    "Remove Readme Assets",
                    $"All contents under {s_ReadmeSourceDirectory} will be removed, are you sure you want to proceed?",
                    "Proceed",
                    "Cancel"))
            {
                return;
            }

            if (Directory.Exists(s_ReadmeSourceDirectory))
            {
                FileUtil.DeleteFileOrDirectory(s_ReadmeSourceDirectory);
                FileUtil.DeleteFileOrDirectory(s_ReadmeSourceDirectory + ".meta");
            }
            else
            {
                Debug.Log($"Could not find the Readme folder at {s_ReadmeSourceDirectory}");
            }

            AssetDatabase.Refresh();
        }

        protected override void OnHeaderGUI()
        {
            var readme = (Readme)target;
            Init();

            var iconWidth = Mathf.Min(EditorGUIUtility.currentViewWidth / 3f - 20f, 128f);

            GUILayout.BeginHorizontal("In BigTitle");

            if (readme.icon != null)
            {
                GUILayout.Space(k_Space);
                GUILayout.Label(readme.icon, GUILayout.Width(iconWidth), GUILayout.Height(iconWidth));
            }

            GUILayout.Space(k_Space);
            GUILayout.BeginVertical();
            GUILayout.FlexibleSpace();
            GUILayout.Label(readme.title, TitleStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        public override void OnInspectorGUI()
        {
            var readme = (Readme)target;
            Init();

            foreach (var section in readme.sections)
            {
                if (!string.IsNullOrEmpty(section.heading))
                {
                    GUILayout.Label(section.heading, HeadingStyle);
                }

                if (!string.IsNullOrEmpty(section.text))
                {
                    GUILayout.Label(section.text, BodyStyle);
                }

                if (!string.IsNullOrEmpty(section.linkText) && LinkLabel(new GUIContent(section.linkText)))
                {
                    Application.OpenURL(section.url);
                }

                GUILayout.Space(k_Space);
            }

            if (GUILayout.Button("Remove Readme Assets", ButtonStyle))
            {
                RemoveTutorial();
            }
        }

        void Init()
        {
            if (m_Initialized)
            {
                return;
            }

            m_BodyStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true,
                fontSize = 14,
                richText = true
            };

            m_TitleStyle = new GUIStyle(m_BodyStyle)
            {
                fontSize = 26
            };

            m_HeadingStyle = new GUIStyle(m_BodyStyle)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 18
            };

            m_LinkStyle = new GUIStyle(m_BodyStyle)
            {
                wordWrap = false,
                stretchWidth = false
            };
            m_LinkStyle.normal.textColor = new Color(0x00 / 255f, 0x78 / 255f, 0xDA / 255f, 1f);

            m_ButtonStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fontStyle = FontStyle.Bold
            };

            m_Initialized = true;
        }

        bool LinkLabel(GUIContent label, params GUILayoutOption[] options)
        {
            var position = GUILayoutUtility.GetRect(label, LinkStyle, options);

            Handles.BeginGUI();
            Handles.color = LinkStyle.normal.textColor;
            Handles.DrawLine(new Vector3(position.xMin, position.yMax), new Vector3(position.xMax, position.yMax));
            Handles.color = Color.white;
            Handles.EndGUI();

            EditorGUIUtility.AddCursorRect(position, MouseCursor.Link);

            return GUI.Button(position, label, LinkStyle);
        }
    }
}
