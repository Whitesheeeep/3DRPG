namespace WS_Modules.Utilities
{
    public abstract class UniqueIdObject : IUniqueId
    {
        public long Id { get; private set; } = IdGenerator.Next();

        public bool IsIdValid => Id != IdGenerator.InvalidId;

        public void ReleaseId()
        {
            Id = IdGenerator.InvalidId;
        }
    }
}
