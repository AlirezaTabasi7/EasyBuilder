using UnityEngine;

namespace EasyBuilder
{
    [CreateAssetMenu(menuName = "Easy Builder/New Sample Profile")]
    public class SampleBuildProfileSO : BaseBuildProfileSO
    {
        public override void ApplyProfile()
        {
            // Call the base first: it records the current settings so they can be restored afterwards.
            base.ApplyProfile();

            // Your logic goes here...
        }

        public override void RevertProfile()
        {
            // Undo your changes here...

            base.RevertProfile();
        }
    }
}
