using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VLat.Core.Model;
using VLat.Core.Serialization;

namespace VLat.Runtime
{
    internal sealed class RuntimeTextureSet : IDisposable
    {
        private readonly bool _ownsTextures;
        private bool _disposed;

        private RuntimeTextureSet(Texture[] textures, bool ownsTextures)
        {
            Textures = textures;
            _ownsTextures = ownsTextures;
        }

        public Texture[] Textures { get; }

        public static RuntimeTextureSet FromCaller(Texture[] textures)
        {
            return new RuntimeTextureSet(textures ?? Array.Empty<Texture>(), false);
        }

        public static RuntimeTextureSet Load(
            VlatDocument document,
            IReadOnlyList<byte[]> binaryBuffers,
            string jsonPath,
            VlatFileAccessPolicy policy,
            List<VlatRuntimeDiagnostic> diagnostics)
        {
            policy = policy ?? VlatFileAccessPolicy.Default;
            var textures = new Texture[document.Textures.Count];
            var budget = new VlatTextureBudget(policy.Limits);
            for (var index = 0; index < document.Textures.Count; index++)
            {
                var definition = document.Textures[index];
                var path = "$.textures[" + index + "]";
                if (definition == null)
                {
                    diagnostics.Add(new VlatRuntimeDiagnostic(
                        VlatRuntimeDiagnosticSeverity.Error,
                        path,
                        "texture定義がnullです。"));
                    continue;
                }

                byte[] encoded;
                string error;
                try
                {
                    if (!string.IsNullOrEmpty(definition.Uri))
                    {
                        // bufferと同じアクセス範囲・上限で外部画像を読む（Core共通の解決規則）。
                        string texturePath;
                        if (!VlatExternalFiles.TryResolve(jsonPath, definition.Uri, policy, out texturePath, out error) ||
                            !VlatExternalFiles.TryReadAllBytes(texturePath, policy.Limits.MaxTextureEncodedBytes, out encoded, out error))
                        {
                            throw new InvalidDataException(error);
                        }
                    }
                    else
                    {
                        if (!definition.BufferView.HasValue)
                        {
                            throw new InvalidDataException("textureにはuriまたはbufferViewが必要です。");
                        }

                        encoded = CopyBufferView(document, binaryBuffers, definition.BufferView.Value, budget);
                    }

                    // デコード前にヘッダーの寸法と、全textureのデコード後合計を確認する。
                    if (!budget.TryReserve(encoded, out error))
                    {
                        throw new InvalidDataException(error);
                    }
                }
                catch (Exception exception)
                {
                    diagnostics.Add(new VlatRuntimeDiagnostic(
                        VlatRuntimeDiagnosticSeverity.Error,
                        path,
                        "texture画像を読み込めません: " + exception.Message));
                    continue;
                }

                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.DontSave,
                };
                texture.name = string.IsNullOrEmpty(definition.Name) ? "Kakudoll Texture " + index : definition.Name;
                if (!ImageConversion.LoadImage(texture, encoded, true))
                {
                    DestroyObject(texture);
                    diagnostics.Add(new VlatRuntimeDiagnostic(
                        VlatRuntimeDiagnosticSeverity.Error,
                        path,
                        "対応する画像形式としてデコードできません。"));
                    continue;
                }

                textures[index] = texture;
            }

            return new RuntimeTextureSet(textures, true);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (!_ownsTextures)
            {
                return;
            }

            for (var index = 0; index < Textures.Length; index++)
            {
                if (Textures[index] != null)
                {
                    DestroyObject(Textures[index]);
                    Textures[index] = null;
                }
            }
        }

        private static byte[] CopyBufferView(
            VlatDocument document,
            IReadOnlyList<byte[]> binaryBuffers,
            int viewIndex,
            VlatTextureBudget budget)
        {
            if (viewIndex < 0 || viewIndex >= document.BufferViews.Count)
            {
                throw new InvalidDataException("bufferView参照が範囲外です。");
            }

            var view = document.BufferViews[viewIndex];
            if (view == null || view.Buffer < 0 || view.Buffer >= binaryBuffers.Count)
            {
                throw new InvalidDataException("bufferViewのbuffer参照が範囲外です。");
            }

            var source = binaryBuffers[view.Buffer];
            if (source == null
                || view.ByteOffset < 0
                || view.ByteLength < 0
                || view.ByteOffset > source.LongLength
                || view.ByteLength > source.LongLength - view.ByteOffset
                || view.ByteOffset > int.MaxValue
                || view.ByteLength > int.MaxValue)
            {
                throw new InvalidDataException("bufferViewのバイト範囲が不正です。");
            }

            string sizeError;
            if (!budget.CheckEncodedLength(view.ByteLength, out sizeError))
            {
                throw new InvalidDataException(sizeError);
            }

            var destination = new byte[(int)view.ByteLength];
            Buffer.BlockCopy(source, (int)view.ByteOffset, destination, 0, destination.Length);
            return destination;
        }

        private static void DestroyObject(UnityEngine.Object target)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
