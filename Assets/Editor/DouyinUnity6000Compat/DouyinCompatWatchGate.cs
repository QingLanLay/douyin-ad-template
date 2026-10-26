using System;

namespace DouyinUnity6000Compat
{
    /// <summary>纯 .NET 产物稳定门控，时钟和签名由调用方提供。</summary>
    public sealed class DouyinCompatWatchGate
    {
        private const double StableSeconds = 2d;
        private string candidateSignature;
        private double candidateSince;
        private string processedSignature;

        /// <summary>同一非空签名连续稳定至少两秒且非忙碌时，仅触发一次。</summary>
        public bool Observe(string signature, double now, bool busy)
        {
            if (busy || string.IsNullOrEmpty(signature) || double.IsNaN(now) || double.IsInfinity(now))
            {
                ClearCandidate();
                return false;
            }

            if (string.Equals(signature, processedSignature, StringComparison.Ordinal))
            {
                ClearCandidate();
                return false;
            }

            if (!string.Equals(signature, candidateSignature, StringComparison.Ordinal) || now < candidateSince)
            {
                candidateSignature = signature;
                candidateSince = now;
                return false;
            }

            if (now - candidateSince >= StableSeconds)
            {
                MarkProcessed(signature);
                return true;
            }
            return false;
        }

        /// <summary>记住兼容处理后的新签名，避免把自身写入再次当成待处理变化。</summary>
        public void MarkProcessed(string signature)
        {
            if (!string.IsNullOrEmpty(signature))
                processedSignature = signature;
            ClearCandidate();
        }

        /// <summary>清空候选及已处理签名，用于需要重新检查现有产物的场合。</summary>
        public void Reset()
        {
            processedSignature = null;
            ClearCandidate();
        }

        private void ClearCandidate()
        {
            candidateSignature = null;
            candidateSince = 0d;
        }
    }
}
