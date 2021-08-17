﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using CommunityToolkit.WinUI.Helpers;
using Microsoft.Toolkit.Diagnostics;
using Microsoft.Toolkit.HighPerformance.Buffers;
using Pixeval.Util;
using Functions = Pixeval.Util.Functions;

namespace Pixeval
{

    public class FileCache
    {
        private readonly Type[] _supportedKeyTypes;
        private const string IndexFileName = "index.json";
        private const string CacheFolderName = "cache";
        private Dictionary<Guid, string> _index;
        private readonly StorageFolder _baseDirectory;
        private StorageFile? _indexFile;
        private StorageFile? _expireIndexFile;
        private readonly ReaderWriterLockSlim _indexLocker;
        private const string ExpireIndexFileName = "eindex.json";
        // The expiration time
        private Dictionary<Guid, DateTimeOffset> _expireIndex;
        private readonly ReaderWriterLockSlim _expireIndexLocker;

        public static FileCache Default { get; }

        public int HitCount { get; private set; }

        static FileCache()
        {
            Default = new FileCache();
        }

        private FileCache()
        {
            _supportedKeyTypes = new[] {typeof(int), typeof(uint), typeof(ulong), typeof(long)};
            _baseDirectory = ApplicationData.Current.TemporaryFolder.GetOrCreateFolderAsync(CacheFolderName).ConfiguredWait();

            _index = new Dictionary<Guid, string>();
            _indexLocker = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
            _expireIndex = new Dictionary<Guid, DateTimeOffset>();
            _expireIndexLocker = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
            LoadIndex();
            WriteIndex();
        }

        public bool AutoExpire { get; set; }

        /// <summary>
        /// Adds an entry to the cache
        /// </summary>
        /// <param name="key">Unique identifier for the entry</param>
        /// <param name="data">Data object to store</param>
        /// <param name="expireIn">Time from UtcNow to expire entry in</param>
        /// <param name="eTag">Optional eTag information</param>
        public async Task Add(Guid key, object data, TimeSpan expireIn, string? eTag = null)
        {
            _indexLocker.EnterWriteLock();
            _expireIndexLocker.EnterWriteLock();
            try
            {
                var file = await _baseDirectory.GetOrCreateFileAsync(key.ToString("N"));
                switch (data)
                {
                    case byte[] bytes:
                        await file.WriteBytesAsync(bytes);
                        break;
                    case Stream stream:
                        await using (var fs = await file.OpenStreamForWriteAsync())
                        {
                            await stream.CopyToAsync(fs);
                        }
                        break;
                    default:
                        await file.WriteBytesAsync(JsonSerializer.SerializeToUtf8Bytes(data));
                        break;
                }

                _index[key] = eTag ?? string.Empty;
                _expireIndex[key] = GetExpiration(expireIn);

                WriteIndex();
                WriteExpireIndex();
            }
            finally
            {
                _indexLocker.ExitWriteLock();
                _expireIndexLocker.ExitWriteLock();
            }
        }

        /// <summary>
        /// Adds an entry to the cache
        /// </summary>
        /// <param name="key">Unique identifier for the entry</param>
        /// <param name="data">Data object to store</param>
        /// <param name="expireIn">Time from UtcNow to expire entry in</param>
        /// <param name="eTag">Optional eTag information</param>
        public async Task Add(object key, object data, TimeSpan expireIn, string? eTag = null)
        {
            Guard.IsNotNull(key, nameof(key));
            Guard.IsNotNullOrEmpty(key as string, nameof(key));
            Guard.IsNotNull(data, nameof(data));
            
            await Add(key switch
            {
                Guid g => g,
                string s => Guid.Parse(s),
                _ when _supportedKeyTypes.Contains(key.GetType()) => HashToGuid(key),
                _ => throw new ArgumentOutOfRangeException(nameof(key), key, null)
            }, data, expireIn, eTag);
        }

        /// <summary>
        /// Adds an entry to the cache
        /// </summary>
        /// <param name="key">Unique identifier for the entry</param>
        /// <param name="data">Data object to store</param>
        /// <param name="expireIn">Time from UtcNow to expire entry in</param>
        /// <param name="eTag">Optional eTag information</param>
        public Task Add(string key, object data, TimeSpan expireIn, string? eTag = null)
        {
            Guard.IsNotNull(key, nameof(key));
            Guard.IsNotNull(data, nameof(data));

            return Add(HashToGuid(key), data, expireIn, eTag);
        }

        /// <summary>
        /// Empties all specified entries regardless of whether they're expired or not.
        /// Throws an exception if any deletion fails and rollback changes.
        /// </summary>
        /// <param name="keys">keys to empty</param>
        public Task Empty(params object[] keys)
        {
            Guard.IsNotNull(keys, nameof(keys));

            var arrElementType = keys.GetType().GetElementType();
            return keys switch
            {
                var k when arrElementType == typeof(Guid) => Empty(k.Cast<Guid>()),
                string[] arr => Empty(arr),
                var k when _supportedKeyTypes.Contains(arrElementType) => Empty(k.Select(HashToGuid)),
                _ => throw new ArgumentException(@$"The element type of keys '{keys.GetType().GetElementType()} is not supported.'")
            };
        }

        /// <summary>
        /// Empties all specified entries regardless if they are expired.
        /// Throws an exception if any deletions fail and rolls back changes.
        /// </summary>
        /// <param name="keys">keys to empty</param>
        public async Task Empty(params string[] keys)
        {
            await Empty(keys.Select(HashToGuid));
        }

        /// <summary>
        /// Empties all specified entries regardless if they are expired.
        /// Throws an exception if any deletions fail and rolls back changes.
        /// </summary>
        /// <param name="keys">keys to empty</param>
        public async Task Empty(IEnumerable<Guid> keys)
        {
            _indexLocker.EnterWriteLock();

            try
            {
                foreach (var k in keys)
                {
                    (await _baseDirectory.TryGetItemAsync(HashToGuid(k).ToString("N")))?.DeleteAsync(StorageDeleteOption.PermanentDelete);
                    _index.Remove(k);
                }
                WriteIndex();
            }
            finally
            {
                _indexLocker.ExitWriteLock();
            }
        }

        /// <summary>
        /// Empties all expired entries that are in the cache.
        /// Throws an exception if any deletions fail and rolls back changes.
        /// </summary>
        public async Task EmptyAll()
        {
            _indexLocker.EnterWriteLock();

            try
            {
                await Task.WhenAll(_index.Select(item => HashToGuid(item.Key))
                    .Select(guid => _baseDirectory.TryGetItemAsync(guid.ToString("N")).AsTask())
                    .Select(item => item.ContinueWith(t => t.Result?.DeleteAsync())));
                _index.Clear();
                WriteIndex();
            }
            finally
            {
                _indexLocker.ExitWriteLock();
            }
        }

        /// <summary>
        /// Empties all expired entries that are in the cache.
        /// Throws an exception if any deletions fail and rolls back changes.
        /// </summary>
        public async Task EmptyExpired()
        {
            _expireIndexLocker.EnterWriteLock();
            _indexLocker.EnterWriteLock();

            try
            {
                var expired = _expireIndex.Where(k => k.Value < DateTimeOffset.Now);
                
                foreach (var (key, _) in expired)
                {
                    await (await _baseDirectory.TryGetItemAsync(key.ToString("N")))?.DeleteAsync();
                    _index.Remove(key);
                }

                WriteIndex();
                WriteExpireIndex();
            }
            finally
            {
                _indexLocker.ExitWriteLock();
                _expireIndexLocker.ExitWriteLock();
            }
        }

        /// <summary>
        /// Checks to see if the key exists in the cache.
        /// </summary>
        /// <param name="key">Unique identifier for the entry to check</param>
        /// <returns>If the key exists</returns>
        public bool Exists(object key)
        {
            Guard.IsNotNull(key, nameof(key));

            return key switch
            {
                Guid g => Exists(g),
                string s => Exists(s),
                var k when _supportedKeyTypes.Contains(k.GetType()) => Exists(HashToGuid(k)),
                _ => throw new ArgumentException($"The type of key '{key.GetType()} is not supported.'")
            };
        }

        /// <summary>
        /// Checks to see if the key exists in the cache.
        /// </summary>
        /// <param name="key">Unique identifier for the entry to check</param>
        /// <returns>If the key exists</returns>
        public bool Exists(string key)
        {
            Guard.IsNotNull(key, nameof(key));
            return Exists(HashToGuid(key));
        }

        /// <summary>
        /// Checks to see if the key exists in the cache.
        /// </summary>
        /// <param name="key">Unique identifier for the entry to check</param>
        /// <returns>If the key exists</returns>
        public bool Exists(Guid key)
        {
            _indexLocker.EnterReadLock();

            try
            {
                return _index.ContainsKey(key);
            }
            finally
            {
                _indexLocker.ExitReadLock();
            }
        }

        /// <summary>
        /// Gets all the keys that are saved in the cache
        /// </summary>
        /// <returns>The IEnumerable of keys</returns>
        public IEnumerable<Guid> GetKeys(CacheState state = CacheState.Active)
        {
            _indexLocker.EnterReadLock();

            try
            {
                var bananas = state.HasFlag(CacheState.Active)
                    ? _expireIndex
                        .Where(x => x.Value >= DateTimeOffset.Now)
                        .ToList()
                    : new List<KeyValuePair<Guid, DateTimeOffset>>();

                if (state.HasFlag(CacheState.Expired))
                {
                    bananas.AddRange(_expireIndex.Where(x => x.Value < DateTimeOffset.Now));
                }

                return bananas.Select(x => x.Key);
            }
            catch
            {
                return Enumerable.Empty<Guid>();
            }
            finally
            {
                _indexLocker.ExitReadLock();
            }
        }

        public Task<T?> Get<T>(object key)
        {
            Guard.IsNotNull(key, nameof(key));

            return key switch
            {
                Guid g => Get<T>(g),
                string s => Get<T>(s),
                var k when _supportedKeyTypes.Contains(k.GetType()) => Get<T>(HashToGuid(key)),
                _ => throw new ArgumentException($"The type of key '{key.GetType()} is not supported.'")
            };
        }

        public Task<T?> Get<T>(string key)
        {
            return Get<T>(HashToGuid(key));
        }

        /// <summary>
        /// Gets the data entry for the specified key.
        /// </summary>
        /// <param name="key">Unique identifier for the entry to get</param>
        /// <returns>The data object that was stored if found, else default(T)</returns>
        public async Task<T?> Get<T>(Guid key)
        {
            _indexLocker.EnterReadLock();

            try
            {
                var item = await _baseDirectory.TryGetItemAsync(key.ToString("N"));

                if (_index.ContainsKey(key) && item is StorageFile file && (!AutoExpire || AutoExpire && !IsExpired(key)))
                {
                    HitCount++;
                    return typeof(T) switch
                    {
                        var type when type == typeof(IRandomAccessStream) || type.IsSubclassOf(typeof(IRandomAccessStream)) => (T) await file.OpenAsync(FileAccessMode.Read),
                        var type when type == typeof(byte[]) || type.IsSubclassOf(typeof(IEnumerable<byte>)) => (T) (object) await file.ReadBytesAsync(),
                        _ => await Functions.Block(async () =>
                        {
                            await using var stream = await file.OpenStreamForReadAsync();
                            return await JsonSerializer.DeserializeAsync<T>(stream);
                        })
                    };
                }
            }
            finally
            {
                _indexLocker.ExitReadLock();
            }
            return default;
        }

        public DateTimeOffset? GetExpiration(object key)
        {
            Guard.IsNotNull(key, nameof(key));

            return key switch
            {
                Guid g => GetExpiration(g),
                string s => GetExpiration(s),
                var k when _supportedKeyTypes.Contains(k.GetType()) => GetExpiration(HashToGuid(key)),
                _ => throw new ArgumentException($"The type of key '{key.GetType()} is not supported.'")
            };
        }

        /// <summary>
        /// Gets the DateTime that the item will expire for the specified key.
        /// </summary>
        /// <param name="key">Unique identifier for entry to get</param>
        /// <returns>The expiration date if the key is found, else null</returns>
        public DateTimeOffset? GetExpiration(string key)
        {
            Guard.IsNotNullOrWhiteSpace(key, nameof(key));
            return GetExpiration(Guid.Parse(key));
        }

        /// <summary>
        /// Gets the DateTime that the item will expire for the specified key.
        /// </summary>
        /// <param name="key">Unique identifier for entry to get</param>
        /// <returns>The expiration date if the key is found, else null</returns>
        public DateTimeOffset? GetExpiration(Guid key)
        {
            _expireIndexLocker.EnterReadLock();

            try
            {
                return _expireIndex.TryGetValue(key, out var date) ? date : null;
            }
            finally
            {
                _indexLocker.ExitReadLock();
            }
        }

        /// <summary>
        /// Gets the ETag for the specified key.
        /// </summary>
        /// <param name="key">Unique identifier for entry to get</param>
        /// <returns>The ETag if the key is found, else null</returns>
        public string? GetETag(object key)
        {
            Guard.IsNotNull(key, nameof(key));

            return key switch
            {
                Guid g => GetETag(g),
                string s => GetETag(s),
                var k when _supportedKeyTypes.Contains(k.GetType()) => GetETag(HashToGuid(k)),
                _ => throw new ArgumentException($"The type of key '{key.GetType()} is not supported.'")
            };
        }

        /// <summary>
        /// Gets the ETag for the specified key.
        /// </summary>
        /// <param name="key">Unique identifier for entry to get</param>
        /// <returns>The ETag if the key is found, else null</returns>
        public string? GetETag(string key)
        {
            Guard.IsNotNullOrWhiteSpace(key, nameof(key));
            return GetETag(HashToGuid(key));
        }

        /// <summary>
        /// Gets the ETag for the specified key.
        /// </summary>
        /// <param name="key">Unique identifier for entry to get</param>
        /// <returns>The ETag if the key is found, else null</returns>
        public string? GetETag(Guid key)
        {
            _indexLocker.EnterReadLock();

            try
            {
                return _index.TryGetValue(key, out var tag) ? tag : null;
            }
            finally
            {
                _indexLocker.ExitReadLock();
            }
        }

        /// <summary>
        /// Checks to see if the entry for the key is expired.
        /// </summary>
        /// <param name="key">Key to check</param>
        /// <returns>If the expiration data has been met</returns>
        public bool IsExpired(object key)
        {
            Guard.IsNotNull(key, nameof(key));

            return key switch
            {
                Guid g => IsExpired(g),
                string s => IsExpired(s),
                var k when _supportedKeyTypes.Contains(k.GetType()) => IsExpired(HashToGuid(key)),
                _ => throw new ArgumentException($"The type of key '{key.GetType()} is not supported.'")
            };
        }

        /// <summary>
        /// Checks to see if the entry for the key is expired.
        /// </summary>
        /// <param name="key">Key to check</param>
        /// <returns>If the expiration data has been met</returns>
        public bool IsExpired(string key)
        {
            return IsExpired(HashToGuid(key));
        }

        /// <summary>
        /// Checks to see if the entry for the key is expired.
        /// </summary>
        /// <param name="key">Key to check</param>
        /// <returns>If the expiration data has been met</returns>
        public bool IsExpired(Guid key)
        {
            _expireIndexLocker.EnterReadLock();

            try
            {
                return !_expireIndex.TryGetValue(key, out var date) || date < DateTimeOffset.Now;
            }
            finally
            {
                _expireIndexLocker.ExitReadLock();
            }
        }

        private async Task WriteIndex()
        {
            _indexFile ??= await _baseDirectory.CreateFileAsync(IndexFileName, CreationCollisionOption.ReplaceExisting);
            await _indexFile.WriteBytesAsync(JsonSerializer.SerializeToUtf8Bytes(_index));
        }

        private async Task LoadIndex()
        {
            if (await _baseDirectory.TryGetItemAsync(IndexFileName) is StorageFile file)
            {
                _indexFile = file;
                await using var fileStream = await _indexFile.OpenStreamForReadAsync();
                _index = (await JsonSerializer.DeserializeAsync<Dictionary<Guid, string>>(fileStream))!;
            }
        }

        private async Task WriteExpireIndex()
        {
            _expireIndexFile ??= await _baseDirectory.CreateFileAsync(ExpireIndexFileName, CreationCollisionOption.ReplaceExisting);
            await _expireIndexFile.WriteBytesAsync(JsonSerializer.SerializeToUtf8Bytes(_expireIndex));
        }

        private async Task LoadExpireIndex()
        {
            if (await _baseDirectory.TryGetItemAsync(ExpireIndexFileName) is StorageFile file)
            {
                _expireIndexFile = file;
                await using var fileStream = await _indexFile.OpenStreamForReadAsync();
                _expireIndex = (await JsonSerializer.DeserializeAsync<Dictionary<Guid, DateTimeOffset>>(fileStream))!;
            }
        }

        private static Guid HashToGuid(object input)
        {
            Guard.IsNotNull(input, nameof(input));
            Guard.IsNotOfType<Guid>(input, nameof(input));

            return input switch
            {
                string str => new Guid(HashAndTruncateTo128Bit(Encoding.UTF8.GetBytes(str))),
                byte[] bytes => new Guid(HashAndTruncateTo128Bit(bytes)),
                var number and (int or uint or long or ulong) => Functions.Block(() =>
                {
                    Span<byte> span = stackalloc byte[Marshal.SizeOf(number)];
                    Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(span), number);
                    return new Guid(HashAndTruncateTo128Bit(span));
                }),
                _ => throw new ArgumentException($"The input type '{input.GetType()}' is not supported.")
            };

            static byte[] HashAndTruncateTo128Bit(ReadOnlySpan<byte> span)
            {
                var result = SHA256.HashData(span);
                return new ArraySegment<byte>(result, 0, result.Length)[..128].Array!;
            }
        }

        /// <summary>
        /// Gets the expiration from a timespan
        /// </summary>
        /// <param name="timeSpan"></param>
        /// <returns></returns>
        internal static DateTimeOffset GetExpiration(TimeSpan timeSpan)
        {
            try
            {
                return DateTimeOffset.Now + timeSpan;
            }
            catch
            {
                return timeSpan.Milliseconds < 0 ? DateTimeOffset.MinValue : DateTimeOffset.MaxValue;
            }
        }
    }

    /// <summary>
    /// Current state of the item in the cache.
    /// </summary>
    [Flags]
    public enum CacheState
    {
        /// <summary>
        /// An unknown state for the cache item
        /// </summary>
        None = 0,

        /// <summary>
        /// Expired cache item
        /// </summary>
        Expired = 1,

        /// <summary>
        /// Active non-expired cache item
        /// </summary>
        Active = 2
    }
}