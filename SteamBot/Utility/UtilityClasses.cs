using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Common.Utility {

    public static class Environment {
        public class Scope {
            public readonly bool isCore;

            public Scope(bool isCore) {
                this.isCore = isCore;
            }
        };

        private static Scope scope = null;

        public static Scope GetScope() {
            if (scope == null) {
                throw new ArgumentException("Scope not initialized.");
            }
            return scope;
        }

        public static void InitializeScope(bool isCore) {
            if (scope != null) {
                throw new ArgumentException("Cannot initialize scope twice.");
            }
            scope = new Scope(isCore);
        }
    }

    public static class BinarySerialization {
        static ConcurrentDictionary<string, ReaderWriterLockSlim> fileLock = new ConcurrentDictionary<string, ReaderWriterLockSlim>();
        public static void WriteToBinaryFile<T>(string filePath, T objectToWrite, bool append = false) {
            fileLock.TryAdd(filePath, new ReaderWriterLockSlim());
            if (fileLock.TryGetValue(filePath, out ReaderWriterLockSlim rwlock)) {
                rwlock.EnterWriteLock();
                try {
                    using (Stream stream = File.Open(filePath, append ? FileMode.Append : FileMode.Create)) {
                        var binaryFormatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
                        binaryFormatter.Serialize(stream, objectToWrite);
                    }
                } finally {
                    rwlock.ExitWriteLock();
                }
            }
        }

        public static T ReadFromBinaryFile<T>(string filePath) {
            fileLock.TryAdd(filePath, new ReaderWriterLockSlim());
            if (fileLock.TryGetValue(filePath, out ReaderWriterLockSlim rwlock)) {
                rwlock.EnterReadLock();
                try {
                    using (Stream stream = File.Open(filePath, FileMode.Open)) {
                        var binaryFormatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
                        return (T)binaryFormatter.Deserialize(stream);
                    }
                } finally {
                    rwlock.ExitReadLock();
                }
            }
            return default(T);
        }

        public static class NS {
            public static T Clone<T>(T obj) {
                return Deserialize<T>(Serialize(obj));
            }

            public static void Serialize<T>(string filePath, T objectToWrite, bool gzip = false) {
                NetSerializer.Serializer Serializer = new NetSerializer.Serializer(new Type[] { typeof(T) });
                fileLock.TryAdd(filePath, new ReaderWriterLockSlim());
                if (fileLock.TryGetValue(filePath, out ReaderWriterLockSlim rwlock)) {
                    rwlock.EnterWriteLock();
                    try {
                        using (Stream stream = File.Open(filePath, FileMode.Create)) {
                            if (gzip) {
                                using (GZipStream gzipSteam = new GZipStream(stream, CompressionMode.Compress)) {
                                    Serializer.Serialize(gzipSteam, objectToWrite);
                                }
                            } else {
                                Serializer.Serialize(stream, objectToWrite);
                            }
                        }
                    } finally {
                        rwlock.ExitWriteLock();
                    }
                }
            }

            public static byte[] Serialize<T>(T objectToWrite, bool gzip = false) {
                NetSerializer.Serializer Serializer = new NetSerializer.Serializer(new Type[] { typeof(T) });
                try {
                    using (var stream = new MemoryStream()) {
                        if (gzip) {
                            using (GZipStream gzipSteam = new GZipStream(stream, CompressionMode.Compress)) {
                                Serializer.Serialize(gzipSteam, objectToWrite);
                            }
                        } else {
                            Serializer.Serialize(stream, objectToWrite);
                        }
                        return stream.ToArray();
                    }
                } catch (Exception e) {
                    return null;
                }
            }

            public static T Deserialize<T>(string filePath, bool gzip = false) {
                NetSerializer.Serializer Serializer = new NetSerializer.Serializer(new Type[] { typeof(T) });
                fileLock.TryAdd(filePath, new ReaderWriterLockSlim());
                if (fileLock.TryGetValue(filePath, out ReaderWriterLockSlim rwlock)) {
                    rwlock.EnterReadLock();
                    try {
                        using (Stream stream = File.Open(filePath, FileMode.Open)) {

                            if (gzip) {
                                using (GZipStream gzipSteam = new GZipStream(stream, CompressionMode.Decompress)) {
                                    return (T)Serializer.Deserialize(gzipSteam);
                                }
                            }
                            return (T)Serializer.Deserialize(stream);
                        }
                    } finally {
                        rwlock.ExitReadLock();
                    }
                }
                return default(T);
            }

            public static T Deserialize<T>(byte[] arr, bool gzip = false) {
                NetSerializer.Serializer Serializer = new NetSerializer.Serializer(new Type[] { typeof(T) });
                try {
                    using (MemoryStream memoryStream = new MemoryStream(arr)) {
                        if (gzip) {
                            using (GZipStream gzipSteam = new GZipStream(memoryStream, CompressionMode.Decompress)) {
                                return (T)Serializer.Deserialize(gzipSteam);
                            }
                        }
                        return (T)Serializer.Deserialize(memoryStream);
                    }
                } catch {
                    return default(T);
                }
            }
        }
    }

    public static class JsonSerialization {
        public static void WriteToJsonFile<T>(string filePath, T objectToWrite, bool append = false) where T : new() {
            TextWriter writer = null;
            try {
                var contentsToWriteToFile =
                    Newtonsoft.Json.JsonConvert.SerializeObject(objectToWrite, Newtonsoft.Json.Formatting.Indented);
                writer = new StreamWriter(filePath, append);
                writer.Write(contentsToWriteToFile);
            } finally {
                if (writer != null)
                    writer.Close();
            }
        }

        public static T ReadFromJsonFile<T>(string filePath) where T : new() {
            TextReader reader = null;
            try {
                reader = new StreamReader(filePath);
                var fileContents = reader.ReadToEnd();
                return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(fileContents);
            } finally {
                if (reader != null)
                    reader.Close();
            }
        }
    }

    public static class StringUtils {
        public static string ToBase64(byte[] array) {
            return Convert.ToBase64String(array);
        }

        public static byte[] FromBase64(string text) {
            try {
                byte[] textAsBytes = Convert.FromBase64String(text);
                return textAsBytes;
            } catch (Exception) {
                return null;
            }
        }
    }
}
