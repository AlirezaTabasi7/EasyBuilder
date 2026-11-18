using System;
using System.Reflection;
using UnityEngine;

namespace EasyBuilder
{
    /// <summary>
    /// Runs Google's External Dependency Manager (EDM4U) Android resolver when it is in the project.
    /// It is looked up by reflection, so no scripting define is needed and nothing breaks without it.
    /// </summary>
    internal static class AndroidDependencyResolver
    {
        private const string ResolverTypeName = "GooglePlayServices.PlayServicesResolver";

        private static Type _resolverType;
        private static bool _searched;

        public static bool IsAvailable => FindResolverType() != null;

        /// <summary>Force-resolves Android dependencies and waits for it to finish.</summary>
        /// <returns>False if resolution ran and failed.</returns>
        public static bool ForceResolve(out string error)
        {
            error = null;
            var type = FindResolverType();

            if (type == null)
                return true;

            try
            {
                // ResolveSync blocks until resolution finishes, unlike the menu command, which can
                // still be running when the build starts.
                var resolveSync = type.GetMethod("ResolveSync", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(bool) }, null);

                if (resolveSync != null)
                {
                    Debug.Log("[EasyBuilder] Force-resolving Android dependencies (EDM4U)...");

                    if (resolveSync.Invoke(null, new object[] { true }) is false)
                    {
                        error = "EDM4U Android dependency resolution failed. See the Console for details.";
                        return false;
                    }

                    return true;
                }

                var menuForceResolve = type.GetMethod("MenuForceResolve", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);

                if (menuForceResolve != null)
                {
                    Debug.LogWarning("[EasyBuilder] This EDM4U version has no ResolveSync; using MenuForceResolve, which may still be running when the build starts.");
                    menuForceResolve.Invoke(null, null);
                    return true;
                }

                Debug.LogWarning("[EasyBuilder] EDM4U was found but has no known resolve method. Skipping dependency resolution.");
                return true;
            }
            catch (TargetInvocationException e)
            {
                error = "EDM4U Android dependency resolution threw: " + (e.InnerException ?? e).Message;
                Debug.LogException(e.InnerException ?? e);
                return false;
            }
        }

        private static Type FindResolverType()
        {
            if (_searched)
                return _resolverType;

            _searched = true;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                _resolverType = assembly.GetType(ResolverTypeName, false);

                if (_resolverType != null)
                    break;
            }

            return _resolverType;
        }
    }
}
