namespace EasyBuilder
{
    /// <summary>Settings that are applied before a build and reverted after it.</summary>
    public interface IBuildProfile
    {
        void ApplyProfile();
        void RevertProfile();
    }
}
