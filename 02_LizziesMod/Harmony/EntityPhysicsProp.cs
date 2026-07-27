namespace LizziesMod
{
    public class EntityPhysicsProp : EntityFallingBlock
    {
        public override void OnUpdateEntity()
        {
            // EntityFallingBlock applies impact damage and turns stationary blocks back into world blocks.
            // Entity.Update still owns physics and network movement before this hook is called.
            firstUpdate = false;
        }
    }
}