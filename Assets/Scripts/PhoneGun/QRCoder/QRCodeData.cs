// 出典: QRCoder (https://github.com/codebude/QRCoder) v1.4.3 を元に、
// Unity向けに圧縮/シリアライズ機能（GZip・Deflate関連）を取り除いた最小構成。
// QRCoder is licensed under the MIT License.
using System;
using System.Collections;
using System.Collections.Generic;

namespace QRCoder
{
    public class QRCodeData : IDisposable
    {
        public List<BitArray> ModuleMatrix { get; set; }

        public QRCodeData(int version)
        {
            this.Version = version;
            var size = ModulesPerSideFromVersion(version);
            this.ModuleMatrix = new List<BitArray>();
            for (var i = 0; i < size; i++)
                this.ModuleMatrix.Add(new BitArray(size));
        }

        public int Version { get; private set; }

        private static int ModulesPerSideFromVersion(int version)
        {
            return 21 + (version - 1) * 4;
        }

        public void Dispose()
        {
            this.ModuleMatrix = null;
            this.Version = 0;
        }
    }
}
