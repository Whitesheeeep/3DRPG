namespace RPG.CameraSystem
{
    /// <summary>
    /// 定义技能摄像机修饰请求的创建、逐帧更新和释放边界。
    /// </summary>
    public interface ICameraModifierService
    {
        /// <summary>创建层级固定的新请求；后创建请求位于更高层。</summary>
        CameraModifierHandle CreateModifier(string debugName);
        /// <summary>更新请求当前帧已经求值的修饰状态。</summary>
        void UpdateModifier(CameraModifierHandle handle, CameraModifierState state);
        /// <summary>暂时停用请求，但保留其创建层级。</summary>
        void DeactivateModifier(CameraModifierHandle handle);
        /// <summary>永久释放请求。</summary>
        void ReleaseModifier(CameraModifierHandle handle);
    }
}
