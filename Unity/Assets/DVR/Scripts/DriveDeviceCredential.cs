using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace DVR
{
    internal sealed class DriveDeviceCredential
    {
        private readonly string origin;
        private readonly string preference;

        internal DriveDeviceCredential(string serviceOrigin)
        {
            origin = serviceOrigin;
            using var sha = SHA256.Create();
            preference = "dvr.drive.session." + Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(origin)));
        }

        internal string Load()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var value = PlayerPrefs.GetString(preference, "");
            if (value.Length == 0) return null;
            try
            {
                var encoded = value.Split('.');
                using var cipher = Cipher(2, SignedBytes(Convert.FromBase64String(encoded[0])));
                return Encoding.UTF8.GetString(Bytes(cipher.Call<sbyte[]>("doFinal", SignedBytes(Convert.FromBase64String(encoded[1])))));
            }
            catch (Exception error) when (error is AndroidJavaException || error is FormatException || error is IndexOutOfRangeException)
            {
                Delete();
            }
#endif
            return null;
        }

        internal bool Save(string bearer)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var cipher = Cipher(1, null);
                var encrypted = cipher.Call<sbyte[]>("doFinal", SignedBytes(Encoding.UTF8.GetBytes(bearer)));
                PlayerPrefs.SetString(preference, Convert.ToBase64String(Bytes(cipher.Call<sbyte[]>("getIV")))
                    + "." + Convert.ToBase64String(Bytes(encrypted)));
                PlayerPrefs.Save();
                return true;
            }
            catch (AndroidJavaException)
            {
                Delete();
            }
#endif
            return false;
        }

        internal void Delete()
        {
            PlayerPrefs.DeleteKey(preference);
            PlayerPrefs.Save();
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject Cipher(int mode, sbyte[] iv)
        {
            const string alias = "DVR Google Drive session";
            using var keyStoreType = new AndroidJavaClass("java.security.KeyStore");
            using var keyStore = keyStoreType.CallStatic<AndroidJavaObject>("getInstance", "AndroidKeyStore");
            var load = AndroidJNI.GetMethodID(keyStore.GetRawClass(), "load", "(Ljava/io/InputStream;[C)V");
            AndroidJNI.CallVoidMethod(keyStore.GetRawObject(), load, new jvalue[2]);

            if (!keyStore.Call<bool>("containsAlias", alias))
            {
                using var keyGeneratorType = new AndroidJavaClass("javax.crypto.KeyGenerator");
                using var generator = keyGeneratorType.CallStatic<AndroidJavaObject>("getInstance", "AES", "AndroidKeyStore");
                using var builder = new AndroidJavaObject("android.security.keystore.KeyGenParameterSpec$Builder", alias, 3);
                using var blockMode = builder.Call<AndroidJavaObject>("setBlockModes", (object)new[] { "GCM" });
                using var padding = builder.Call<AndroidJavaObject>("setEncryptionPaddings", (object)new[] { "NoPadding" });
                using var length = builder.Call<AndroidJavaObject>("setKeySize", 256);
                using var specification = builder.Call<AndroidJavaObject>("build");
                generator.Call("init", specification);
                using var generatedKey = generator.Call<AndroidJavaObject>("generateKey");
            }

            using var key = keyStore.Call<AndroidJavaObject>("getKey", alias, Array.Empty<char>());
            using var cipherType = new AndroidJavaClass("javax.crypto.Cipher");
            var cipher = cipherType.CallStatic<AndroidJavaObject>("getInstance", "AES/GCM/NoPadding");
            try
            {
                if (iv == null) cipher.Call("init", mode, key);
                else
                {
                    using var parameters = new AndroidJavaObject("javax.crypto.spec.GCMParameterSpec", 128, iv);
                    cipher.Call("init", mode, key, parameters);
                }
                cipher.Call("updateAAD", SignedBytes(Encoding.UTF8.GetBytes(origin)));
                return cipher;
            }
            catch
            {
                cipher.Dispose();
                throw;
            }
        }

        private static sbyte[] SignedBytes(byte[] bytes)
        {
            var result = new sbyte[bytes.Length];
            Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
            return result;
        }

        private static byte[] Bytes(sbyte[] bytes)
        {
            var result = new byte[bytes.Length];
            Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
            return result;
        }
#endif
    }
}
