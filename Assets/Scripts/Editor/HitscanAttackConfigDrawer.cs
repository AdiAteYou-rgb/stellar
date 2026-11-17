using UnityEditor;
using UnityEngine;
using Stellar.Combat;

namespace Stellar.Editor
{
    /// <summary>
    /// Custom Inspector drawer for HitscanAttackConfig that shows the Max Range field
    /// only when Has Max Range is enabled.
    /// </summary>
    [CustomPropertyDrawer(typeof(HitscanAttackConfig))]
    public sealed class HitscanAttackConfigDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded)
            {
                return EditorGUIUtility.singleLineHeight;
            }

            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            SerializedProperty hasMaxRangeProp = property.FindPropertyRelative("hasMaxRange");
            SerializedProperty onHitscanFiredProp = property.FindPropertyRelative("onHitscanFired");

            // Foldout + abilityName + hasMaxRange + (optional maxRange) + visualEffectPrefab + visualEffectLifetime + onHitscanFired
            float height = line + spacing; // foldout
            height += line + spacing; // abilityName
            height += line + spacing; // hasMaxRange
            if (hasMaxRangeProp != null && hasMaxRangeProp.boolValue)
            {
                height += line + spacing; // maxRange
            }
            height += line + spacing; // visualEffectPrefab
            height += line + spacing; // visualEffectLifetime
            if (onHitscanFiredProp != null)
            {
                height += EditorGUI.GetPropertyHeight(onHitscanFiredProp, true) + spacing;
            }

            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            SerializedProperty abilityNameProp = property.FindPropertyRelative("abilityName");
            string headerLabel = !string.IsNullOrEmpty(abilityNameProp?.stringValue)
                ? abilityNameProp.stringValue
                : label.text;

            Rect foldoutRect = new Rect(position.x, position.y, position.width, line);
            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, headerLabel, true);

            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                float y = position.y + line + spacing;

                DrawField(position.x, ref y, position.width, line, spacing, abilityNameProp);

                SerializedProperty hasMaxRangeProp = property.FindPropertyRelative("hasMaxRange");
                DrawField(position.x, ref y, position.width, line, spacing, hasMaxRangeProp);

                if (hasMaxRangeProp != null && hasMaxRangeProp.boolValue)
                {
                    SerializedProperty maxRangeProp = property.FindPropertyRelative("maxRange");
                    DrawField(position.x, ref y, position.width, line, spacing, maxRangeProp);
                    if (maxRangeProp != null && maxRangeProp.floatValue < 0.01f)
                    {
                        maxRangeProp.floatValue = 0.01f;
                    }
                }

                DrawField(position.x, ref y, position.width, line, spacing, property.FindPropertyRelative("visualEffectPrefab"));
                DrawField(position.x, ref y, position.width, line, spacing, property.FindPropertyRelative("visualEffectLifetime"));

                SerializedProperty onHitscanFiredProp = property.FindPropertyRelative("onHitscanFired");
                if (onHitscanFiredProp != null)
                {
                    float eventHeight = EditorGUI.GetPropertyHeight(onHitscanFiredProp, true);
                    Rect eventRect = new Rect(position.x, y, position.width, eventHeight);
                    EditorGUI.PropertyField(eventRect, onHitscanFiredProp, true);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }

        private static void DrawField(float x, ref float y, float width, float lineHeight, float spacing, SerializedProperty prop)
        {
            if (prop == null) return;
            Rect rect = new Rect(x, y, width, lineHeight);
            EditorGUI.PropertyField(rect, prop, true);
            y += lineHeight + spacing;
        }
    }
}
