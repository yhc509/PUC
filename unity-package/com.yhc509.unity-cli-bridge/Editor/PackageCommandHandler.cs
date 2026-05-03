#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityCli.Protocol;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace UnityCliBridge.Bridge.Editor
{
    internal sealed class PackageCommandHandler
    {
        public bool CanHandle(string command)
        {
            return string.Equals(command, ProtocolConstants.CommandPackageList, StringComparison.Ordinal)
                || string.Equals(command, ProtocolConstants.CommandPackageAdd, StringComparison.Ordinal)
                || string.Equals(command, ProtocolConstants.CommandPackageRemove, StringComparison.Ordinal)
                || string.Equals(command, ProtocolConstants.CommandPackageSearch, StringComparison.Ordinal);
        }

        public string Handle(string command, string argumentsJson)
        {
            if (string.Equals(command, ProtocolConstants.CommandPackageList, StringComparison.Ordinal))
            {
                return HandleList();
            }

            if (string.Equals(command, ProtocolConstants.CommandPackageAdd, StringComparison.Ordinal))
            {
                return HandleAdd(argumentsJson);
            }

            if (string.Equals(command, ProtocolConstants.CommandPackageRemove, StringComparison.Ordinal))
            {
                return HandleRemove(argumentsJson);
            }

            if (string.Equals(command, ProtocolConstants.CommandPackageSearch, StringComparison.Ordinal))
            {
                return HandleSearch(argumentsJson);
            }

            throw new InvalidOperationException("지원하지 않는 package 명령입니다: " + command);
        }

        private string HandleList()
        {
            ListRequest request = Client.List(true);
            WaitForRequest(request);

            if (request.Status == StatusCode.Failure)
            {
                throw new CommandFailureException(
                    "PACKAGE_LIST_FAILED",
                    request.Error?.message ?? "패키지 목록 조회에 실패했습니다.",
                    false,
                    request.Error?.errorCode.ToString());
            }

            var records = new List<PackageRecord>();
            foreach (var package in request.Result)
            {
                records.Add(new PackageRecord
                {
                    name = package.name,
                    version = package.version,
                    displayName = package.displayName ?? package.name,
                    source = package.source.ToString(),
                });
            }

            return ProtocolJson.Serialize(new PackageListPayload
            {
                packages = records.OrderBy(record => record.name, StringComparer.OrdinalIgnoreCase).ToArray(),
            });
        }

        private string HandleAdd(string argumentsJson)
        {
            PackageAddArgs args = ProtocolJson.Deserialize<PackageAddArgs>(argumentsJson) ?? new PackageAddArgs();
            if (string.IsNullOrWhiteSpace(args.name))
            {
                throw new CommandFailureException("INVALID_ARGS", "패키지 이름이 필요합니다.", false, null);
            }

            string identifier = !string.IsNullOrWhiteSpace(args.version)
                ? $"{args.name}@{args.version}"
                : args.name;

            AddRequest request = Client.Add(identifier);
            WaitForRequest(request);

            if (request.Status == StatusCode.Failure)
            {
                throw new CommandFailureException(
                    "PACKAGE_ADD_FAILED",
                    request.Error?.message ?? $"패키지 추가에 실패했습니다: {identifier}",
                    false,
                    request.Error?.errorCode.ToString());
            }

            return ProtocolJson.Serialize(new PackageMutationPayload
            {
                name = request.Result.name,
                version = request.Result.version,
                added = true,
            });
        }

        private string HandleRemove(string argumentsJson)
        {
            PackageRemoveArgs args = ProtocolJson.Deserialize<PackageRemoveArgs>(argumentsJson) ?? new PackageRemoveArgs();
            if (!args.force)
            {
                throw new CommandFailureException("PACKAGE_FORCE_REQUIRED", "Package removal requires --force.");
            }

            if (string.IsNullOrWhiteSpace(args.name))
            {
                throw new CommandFailureException("INVALID_ARGS", "패키지 이름이 필요합니다.", false, null);
            }

            RemoveRequest request = Client.Remove(args.name);
            WaitForRequest(request);

            if (request.Status == StatusCode.Failure)
            {
                throw new CommandFailureException(
                    "PACKAGE_REMOVE_FAILED",
                    request.Error?.message ?? $"패키지 제거에 실패했습니다: {args.name}",
                    false,
                    request.Error?.errorCode.ToString());
            }

            return ProtocolJson.Serialize(new PackageMutationPayload
            {
                name = args.name,
                removed = true,
            });
        }

        private string HandleSearch(string argumentsJson)
        {
            PackageSearchArgs args = ProtocolJson.Deserialize<PackageSearchArgs>(argumentsJson) ?? new PackageSearchArgs();
            if (string.IsNullOrWhiteSpace(args.query))
            {
                throw new CommandFailureException("INVALID_ARGS", "검색 키워드가 필요합니다.", false, null);
            }

            SearchRequest request = Client.SearchAll();
            WaitForRequest(request);

            if (request.Status == StatusCode.Failure)
            {
                throw new CommandFailureException(
                    "PACKAGE_SEARCH_FAILED",
                    request.Error?.message ?? $"패키지 검색에 실패했습니다: {args.query}",
                    false,
                    request.Error?.errorCode.ToString());
            }

            string query = args.query.Trim();
            var records = new List<PackageRecord>();
            foreach (var package in request.Result)
            {
                string displayName = package.displayName ?? package.name;
                if (package.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0
                    && displayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                records.Add(new PackageRecord
                {
                    name = package.name,
                    version = package.versions.latest ?? string.Empty,
                    displayName = displayName,
                    source = package.source.ToString(),
                });
            }

            return ProtocolJson.Serialize(new PackageSearchPayload
            {
                results = records.OrderBy(record => record.name, StringComparer.OrdinalIgnoreCase).ToArray(),
            });
        }

        private static void WaitForRequest(Request request)
        {
            while (!request.IsCompleted)
            {
                System.Threading.Thread.Sleep(10);
            }
        }
    }
}
