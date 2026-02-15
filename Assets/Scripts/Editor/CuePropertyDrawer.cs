using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(Cue))]
public class CuePropertyDrawer : PropertyDrawer
{
    private static bool IsSpawnerWaveField(string propertyName)
    {
        switch (propertyName)
        {
            case "waveMinCount":
            case "waveMaxCount":
            case "waveMinSeparationDegrees":
            case "waveSpawnInterval":
            case "waveFadeInDuration":
            case "waveDistance":
            case "waveEqualDistribution":
            case "waveUniqueClips":
            case "waveFirstInFrontOfAudience":
                return true;
            default:
                return false;
        }
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if (!property.isExpanded)
            return EditorGUIUtility.singleLineHeight;

        float height = EditorGUIUtility.singleLineHeight;
        bool showWaveFields = property.FindPropertyRelative("overrideSpawnerWave").boolValue;

        SerializedProperty iterator = property.Copy();
        SerializedProperty end = iterator.GetEndProperty();
        bool enterChildren = true;

        while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
        {
            enterChildren = false;

            if (!showWaveFields && IsSpawnerWaveField(iterator.name))
                continue;

            height += EditorGUI.GetPropertyHeight(iterator, true) + EditorGUIUtility.standardVerticalSpacing;
        }

        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        Rect foldoutRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true);

        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;

            bool showWaveFields = property.FindPropertyRelative("overrideSpawnerWave").boolValue;
            float y = foldoutRect.yMax + EditorGUIUtility.standardVerticalSpacing;

            SerializedProperty iterator = property.Copy();
            SerializedProperty end = iterator.GetEndProperty();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
            {
                enterChildren = false;

                if (!showWaveFields && IsSpawnerWaveField(iterator.name))
                    continue;

                float fieldHeight = EditorGUI.GetPropertyHeight(iterator, true);
                Rect fieldRect = new Rect(position.x, y, position.width, fieldHeight);
                EditorGUI.PropertyField(fieldRect, iterator, true);
                y += fieldHeight + EditorGUIUtility.standardVerticalSpacing;
            }

            EditorGUI.indentLevel--;
        }

        EditorGUI.EndProperty();
    }
}
