#if UNITY_EDITOR
using UnityEngine;

namespace RPG.SkillSystem.Editor
{
    internal static class SkillTimelineTickUtility
    {
        /// <summary>
        /// 小刻度间隔的“好看”步长序列，通常用于计算小刻度和大刻度的间隔。
        /// </summary>
        private static readonly int[] NiceSteps = { 1, 2, 5 };


        /// <summary>
        /// 根据当前缩放下的像素/帧计算小刻度间隔，确保小刻度间距不小于 6 像素。
        /// </summary>
        /// <param name="pixelsPerFrame"></param>
        /// <returns></returns>
        public static int GetMinorStep(float pixelsPerFrame) => GetNiceStep(6f / Mathf.Max(0.01f, pixelsPerFrame));

        /// <summary>
        /// 根据当前缩放下的像素/帧计算大刻度间隔，确保大刻度间距不小于 60 像素。
        /// </summary>
        /// <param name="pixelsPerFrame"></param>
        /// <returns></returns>
        public static int GetMajorStep(float pixelsPerFrame) => GetNiceStep(60f / Mathf.Max(0.01f, pixelsPerFrame));

        /// <summary>
        /// 将请求的步长调整为“好看”的步长，通常是 1、2、5 的倍数。
        /// </summary>
        /// <param name="requested">原本所需要的步长</param>
        /// <returns></returns>
        private static int GetNiceStep(float requested)
        {
            if (requested <= 1f) return 1;
            int magnitude = 1;
            while (magnitude * 10 < requested) magnitude *= 10;
            for (int i = 0; i < NiceSteps.Length; i++)
            {
                int step = NiceSteps[i] * magnitude;
                if (step >= requested) return step;
            }
            return 10 * magnitude;
        }
    }
}
#endif