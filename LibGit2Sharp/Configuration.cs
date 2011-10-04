using System;
using System.Collections.Generic;
using System.Globalization;
using LibGit2Sharp.Core;

namespace LibGit2Sharp
{
    /// <summary>
    ///   Provides access to configuration variables for a repository.
    /// </summary>
    public class Configuration : IDisposable
    {
        private readonly string globalConfigPath;
        private readonly string systemConfigPath;

        private readonly Repository repository;

        private ConfigurationSafeHandle systemHandle;
        private ConfigurationSafeHandle globalHandle;
        private ConfigurationSafeHandle localHandle;

        internal Configuration(Repository repository)
        {
            this.repository = repository;

            globalConfigPath = ConvertPath(NativeMethods.git_config_find_global);
            systemConfigPath = ConvertPath(NativeMethods.git_config_find_system);

            Init();
        }

        public bool HasGlobalConfig
        {
            get { return globalConfigPath != null; }
        }

        public bool HasSystemConfig
        {
            get { return systemConfigPath != null; }
        }

        private static string ConvertPath(Func<byte[], int> pathRetriever)
        {
            var buffer = new byte[NativeMethods.GIT_PATH_MAX];

            int result = pathRetriever(buffer);

            //TODO: Make libgit2 return different codes to clearly identify a not found file (GIT_ENOTFOUND ) from any other error (!= GIT_SUCCESS)
            if (result != (int)GitErrorCode.GIT_SUCCESS)
            {
                return null;
            }

            return Utf8Marshaler.Utf8FromBuffer(buffer);
        }

        internal ConfigurationSafeHandle LocalHandle
        {
            get { return localHandle; }
        }

        #region IDisposable Members

        /// <summary>
        ///   Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        ///   Saves any open configuration files.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        /// <summary>
        ///   Delete a configuration variable (key and value).
        /// </summary>
        /// <param name = "key">The key to delete.</param>
        public void Delete(string key)
        {
            Ensure.Success(NativeMethods.git_config_delete(localHandle, key));
        }

        /// <summary>
        ///   Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            localHandle.SafeDispose();
            globalHandle.SafeDispose();
            systemHandle.SafeDispose();
        }

        private static T ProcessReadResult<T>(int res, T value, T defaultValue, Func<object, T> postProcessor = null)
        {
            if (res == (int)GitErrorCode.GIT_ENOTFOUND)
            {
                return defaultValue;
            }

            Ensure.Success(res);

            if (postProcessor == null)
            {
                return value;
            }

            return postProcessor(value);
        }

        private readonly IDictionary<Type, Func<string, object, ConfigurationSafeHandle, object>> configurationTypedRetriever = ConfigurationTypedRetriever();

        private static Dictionary<Type, Func<string, object, ConfigurationSafeHandle, object>> ConfigurationTypedRetriever()
        {
            var dic = new Dictionary<Type, Func<string, object, ConfigurationSafeHandle, object>>();

            dic.Add(typeof(int), (key, dv, handle) =>
                                     {
                                         int value;
                                         int res = NativeMethods.git_config_get_int32(handle, key, out value);
                                         return ProcessReadResult(res, value, dv);
                                     });

            dic.Add(typeof(long), (key, dv, handle) =>
                                      {
                                          long value;
                                          int res = NativeMethods.git_config_get_int64(handle, key, out value);
                                          return ProcessReadResult(res, value, dv);
                                      });

            dic.Add(typeof(bool), (key, dv, handle) =>
                                      {
                                          bool value;
                                          int res = NativeMethods.git_config_get_bool(handle, key, out value);
                                          return ProcessReadResult(res, value, dv);
                                      });

            dic.Add(typeof(string), (key, dv, handle) =>
                                        {
                                            IntPtr value;
                                            int res = NativeMethods.git_config_get_string(handle, key, out value);
                                            return ProcessReadResult(res, value, dv, s => ((IntPtr)s).MarshallAsString());
                                        });

            return dic;
        }

        /// <summary>
        ///   Get a configuration value for a key. Keys are in the form 'section.name'.
        ///   <para>
        ///     For example in  order to get the value for this in a .git\config file:
        /// 
        ///     [core]
        ///     bare = true
        /// 
        ///     You would call:
        /// 
        ///     bool isBare = repo.Config.Get&lt;bool&gt;("core.bare");
        ///   </para>
        /// </summary>
        /// <typeparam name = "T">The configuration value type</typeparam>
        /// <param name = "key">The key</param>
        /// <param name = "defaultValue">The default value (optional)</param>
        /// <returns>The configuration value, or <c>defaultValue</c> if not set</returns>
        public T Get<T>(string key, T defaultValue = default(T))
        {
            if (!configurationTypedRetriever.ContainsKey(typeof(T)))
            {
                throw new ArgumentException(string.Format("Generic Argument of type '{0}' is not supported.", typeof(T).FullName));
            }

            return (T)configurationTypedRetriever[typeof(T)](key, defaultValue, LocalHandle);
        }

        private void Init()
        {
            Ensure.Success(NativeMethods.git_repository_config(out localHandle, repository.Handle, globalConfigPath, systemConfigPath));

            if (globalConfigPath != null)
            {
                Ensure.Success(NativeMethods.git_config_open_ondisk(out globalHandle, globalConfigPath));
            }

            if (systemConfigPath != null)
            {
                Ensure.Success(NativeMethods.git_config_open_ondisk(out systemHandle, systemConfigPath));
            }
        }

        public void Save()
        {
            Dispose(true);
            Init();
        }

        /// <summary>
        ///   Set a configuration value for a key. Keys are in the form 'section.name'.
        ///   <para>
        ///     For example in order to set the value for this in a .git\config file:
        ///   
        ///     [test]
        ///     boolsetting = true
        ///   
        ///     You would call:
        ///   
        ///     repo.Config.Set("test.boolsetting", true);
        ///   </para>
        /// </summary>
        /// <typeparam name = "T"></typeparam>
        /// <param name = "key"></param>
        /// <param name = "value"></param>
        /// <param name = "level"></param>
        public void Set<T>(string key, T value, ConfigurationLevel level = ConfigurationLevel.Local)
        {
            if (level == ConfigurationLevel.Global && !HasGlobalConfig)
            {
                throw new NotSupportedException();
            }

            if (level == ConfigurationLevel.System && !HasSystemConfig)
            {
                throw new NotSupportedException();
            }

            ConfigurationSafeHandle h;

            switch (level)
            {
                case ConfigurationLevel.Local:
                    h = localHandle;
                    break;
                case ConfigurationLevel.Global:
                    h = globalHandle;
                    break;
                case ConfigurationLevel.System:
                    h = systemHandle;
                    break;
                default:
                    throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "Configuration level has an unexpected value ('{0}').", Enum.GetName(typeof(ConfigurationLevel), level)), "level");
            }

            if (typeof(T) == typeof(string))
            {
                Ensure.Success(NativeMethods.git_config_set_string(h, key, (string)(object)value));
                return;
            }

            if (typeof(T) == typeof(bool))
            {
                Ensure.Success(NativeMethods.git_config_set_bool(h, key, (bool)(object)value));
                return;
            }

            if (typeof(T) == typeof(int))
            {
                Ensure.Success(NativeMethods.git_config_set_int32(h, key, (int)(object)value));
                return;
            }

            if (typeof(T) == typeof(long))
            {
                Ensure.Success(NativeMethods.git_config_set_int64(h, key, (long)(object)value));
                return;
            }

            throw new ArgumentException(string.Format("Generic Argument of type '{0}' is not supported.", typeof(T).FullName));
        }
    }
}