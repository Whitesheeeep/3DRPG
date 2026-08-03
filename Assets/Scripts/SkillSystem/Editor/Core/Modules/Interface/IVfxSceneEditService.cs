#if UNITY_EDITOR
using UnityEngine;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 保存从场景编辑代理读取出的 VFX 局部 Transform 草稿，不包含粒子或业务组件参数。
    /// </summary>
    internal readonly struct VfxTransformSnapshot
    {
        public Vector3 LocalPosition { get; }
        public Vector3 LocalEulerAngles { get; }
        public Vector3 LocalScale { get; }

        // 创建相对于冻结绑定矩阵的不可变 Transform 快照。
        internal VfxTransformSnapshot(Vector3 localPosition, Vector3 localEulerAngles,
            Vector3 localScale)
        {
            LocalPosition = localPosition;
            LocalEulerAngles = localEulerAngles;
            LocalScale = localScale;
        }
    }

    /// <summary>
    /// 定义 VFX 模块创建、读取和取消窗口私有场景编辑代理的能力，不直接修改 SkillConfig。
    /// </summary>
    internal interface IVfxSceneEditService
    {
        /// <summary>
        /// 判断指定 Clip 是否正在使用独立场景代理编辑 Transform 草稿。
        /// </summary>
        bool IsEditing(string clipId);

        /// <summary>
        /// 使用最近一次有效预览帧为指定 VFX Clip 创建可选择但不可保存的独立代理。
        /// </summary>
        EditResult BeginEdit(VfxSkillClipConfig clip);

        /// <summary>
        /// 重新选择并定位指定 Clip 当前持有的场景编辑代理。
        /// </summary>
        EditResult SelectProxy(string clipId);

        /// <summary>
        /// 将代理世界 Transform 转换为冻结绑定矩阵下的局部快照。
        /// </summary>
        EditResult Capture(string clipId, out VfxTransformSnapshot snapshot);

        /// <summary>
        /// 销毁未提交的场景编辑代理并丢弃 Transform 草稿。
        /// </summary>
        void CancelEdit();
    }
}
#endif