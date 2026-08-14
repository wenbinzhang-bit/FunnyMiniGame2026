using UnityEngine;

namespace FIMSpace.RagdollAnimatorDemo
{
    public class ClayBeastsExampleSettings : MonoBehaviour
    {
        public float SetFixedTimeStep = 0.01f;
        public float TimeScale = 1f;
        public float GravityY = -9.81f;

        [Tooltip( "Ignore TransparentFX vs TransparentFX collision" )]
        public bool TransparentFXFullIgnore = false;

        bool started = false;

        private void Start()
        {
            started = true;
            UpdateSettings();
        }

        void OnValidate()
        {
            UpdateSettings();
        }

        void UpdateSettings()
        {
            if( started == false ) return;

            Time.timeScale = TimeScale;
            Physics.gravity = new Vector3( Physics.gravity.x, GravityY, Physics.gravity.z );
            Time.fixedDeltaTime = SetFixedTimeStep;

            if( TransparentFXFullIgnore ) Physics.IgnoreLayerCollision( 1, 1, true );
            else // RESTORE FOR DEMO SCENES ON BUILD
            {
                Physics.IgnoreLayerCollision( 1, 1, false );
            }

            Physics.IgnoreLayerCollision( 4, 5, true );
            Physics.IgnoreLayerCollision( 1, 4, true );
        }

#if UNITY_EDITOR

        [UnityEditor.CustomEditor( typeof( ClayBeastsExampleSettings ) )]
        private class ClayBeastsExampleSettingsEditor : UnityEditor.Editor
        {
            public override void OnInspectorGUI()
            {
                UnityEditor.EditorGUILayout.HelpBox( "Setting fixed timestep for more precise physics", UnityEditor.MessageType.Info );
                GUILayout.Space( 2f );
                DrawDefaultInspector();
                GUILayout.Space( 4f );
                UnityEditor.EditorGUILayout.LabelField( "Disabling collision between UI and WATER, TransparentFX and WATER  layer on start!", FIMSpace.FEditor.FGUI_Resources.HeaderBoxStyle );
            }
        }

#endif

    }
}