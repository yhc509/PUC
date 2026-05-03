using System;
using UnityCli.Protocol;
using UnityEditor;
using UnityEngine;

namespace UnityCliBridge.Bridge.Editor
{
    internal static class AssetBackupTransaction
    {
        public static T RunWithBackup<T>(string assetPath, string commandName, Func<T> action)
        {
            return Run(assetPath, commandName, action, useMovedBackup: false);
        }

        public static T RunWithMovedBackup<T>(string assetPath, string commandName, Func<T> action)
        {
            return Run(assetPath, commandName, action, useMovedBackup: true);
        }

        private static T Run<T>(string assetPath, string commandName, Func<T> action, bool useMovedBackup)
        {
            string physicalPath = AssetCommandSupport.GetPhysicalPath(assetPath);
            var options = new FileBackupTransactionOptions
            {
                WarningSink = message => Debug.LogWarning(message),
                Refresh = AssetDatabase.Refresh,
            };

            try
            {
                return useMovedBackup
                    ? FileBackupTransaction.RunWithMovedBackup(physicalPath, commandName, action, options)
                    : FileBackupTransaction.RunWithBackup(physicalPath, commandName, action, options);
            }
            catch (FileBackupTransactionException exception)
            {
                throw new CommandFailureException(exception.ErrorCode, exception.Message, exception.Details);
            }
        }
    }
}
