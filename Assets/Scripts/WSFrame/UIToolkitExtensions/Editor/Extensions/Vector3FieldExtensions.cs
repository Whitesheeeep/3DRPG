using UnityEngine.UIElements;

namespace WS_Modules.UIToolkitExtensions.Editor
{
    public static class Vector3FieldExtensions
    {
        public static void SetIsDelayed(this Vector3Field field, bool value)
        {
            var floatFields = field.Query<FloatField>().ToList();
            foreach (var floatField in floatFields)
            {
                floatField.isDelayed = value;
            }
        }
    }
}