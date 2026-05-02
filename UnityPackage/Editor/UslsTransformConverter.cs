using UnityEngine;

namespace Unity2Snap.Editor
{
    internal static class UslsTransformConverter
    {
        public static UslsTransform FromLocalTransform(Transform transform, UslsExportSettings settings)
        {
            var positionScale = settings.ConvertMetersToCentimeters ? 100f : 1f;
            var position = transform.localPosition * positionScale;
            var rotation = transform.localRotation;
            var scale = transform.localScale;

            if (settings.ConvertUnityToLensHandedness)
            {
                position.z = -position.z;
                rotation = ReflectUnityQuaternionAcrossZ(rotation);
            }

            return new UslsTransform
            {
                position = Vector3ToArray(position),
                rotation = Vector3ToArray(NormalizeEuler(rotation.eulerAngles)),
                scale = Vector3ToArray(scale)
            };
        }

        public static float[] Vector3ToArray(Vector3 value)
        {
            return new[] { value.x, value.y, value.z };
        }

        public static float[] Vector3ToScaledArray(Vector3 value, UslsExportSettings settings)
        {
            var scale = settings.ConvertMetersToCentimeters ? 100f : 1f;
            if (settings.ConvertUnityToLensHandedness)
            {
                value.z = -value.z;
            }

            return Vector3ToArray(value * scale);
        }

        public static float[] SizeToScaledArray(Vector3 value, UslsExportSettings settings)
        {
            var scale = settings.ConvertMetersToCentimeters ? 100f : 1f;
            return Vector3ToArray(value * scale);
        }

        public static Vector3 NormalizeEuler(Vector3 euler)
        {
            return new Vector3(NormalizeAngle(euler.x), NormalizeAngle(euler.y), NormalizeAngle(euler.z));
        }

        private static Quaternion ReflectUnityQuaternionAcrossZ(Quaternion value)
        {
            return new Quaternion(-value.x, -value.y, value.z, value.w);
        }

        private static float NormalizeAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f)
            {
                angle -= 360f;
            }
            else if (angle < -180f)
            {
                angle += 360f;
            }

            return angle;
        }
    }
}
