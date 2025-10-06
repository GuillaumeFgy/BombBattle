using UnityEngine;

namespace ShipSimulation
{
    public class ShipOnWater : MonoBehaviour
    {
        public float bobbingSpeed = 0.5f; // Vertical Oscillation Speed
        public float bobbingAmount = 0.2f; // Vertical Oscillation Amplitude
        public float rotationSpeedX = 10f; // X-Axis Rotation Speed
        public float rotationAmountX = 10f; // X-Axis Rotation Amplitude
        public float rotationSpeedZ = 10f; // Z-Axis Rotation Speed
        public float rotationAmountZ = 10f; // Z-Axis Rotation Amplitude

        private Vector3 startPosition;
        private Quaternion startRotation;
        private float randomOffsetX;
        private float randomOffsetY;
        private float randomOffsetZ;
        private float randomRotationSpeedX;
        private float randomRotationSpeedZ;

        void Start()
        {
            // Maintaining initial position and rotation
            startPosition = transform.position;
            startRotation = transform.rotation;

            // Generating random displacements and velocities
            randomOffsetX = Random.Range(-1000f, 1000f);
            randomOffsetY = Random.Range(-1000f, 1000f);
            randomOffsetZ = Random.Range(-1000f, 1000f);
            randomRotationSpeedX = Random.Range(0.5f, 2f) * rotationSpeedX;
            randomRotationSpeedZ = Random.Range(0.5f, 2f) * rotationSpeedZ;
        }

        void Update()
        {
            // Vertical oscillation with random offset
            float newY = startPosition.y + Mathf.Sin((Time.time + randomOffsetY) * bobbingSpeed) * bobbingAmount;
            
            // Rotate along X and Z axes with random offset
            float newRotationX = Mathf.Sin((Time.time + randomOffsetX) * randomRotationSpeedX) * rotationAmountX;
            float newRotationZ = Mathf.Sin((Time.time + randomOffsetZ) * randomRotationSpeedZ) * rotationAmountZ;

            // Update position and rotation
            transform.position = new Vector3(startPosition.x, newY, startPosition.z);
            transform.rotation = startRotation * Quaternion.Euler(newRotationX, 0, newRotationZ);
        }
    }
}
