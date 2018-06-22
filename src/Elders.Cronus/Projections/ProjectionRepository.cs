﻿using System;
using System.Collections.Generic;
using System.Linq;
using Elders.Cronus.Logging;
using Elders.Cronus.Projections.Snapshotting;
using Elders.Cronus.Projections.Versioning;

namespace Elders.Cronus.Projections
{
    public class ProjectionRepository : IProjectionRepository
    {
        static ILog log = LogProvider.GetLogger(typeof(ProjectionRepository));

        readonly IProjectionStore projectionStore;
        readonly ISnapshotStore snapshotStore;
        readonly ISnapshotStrategy snapshotStrategy;
        readonly InMemoryProjectionVersionStore inMemoryVersionStore;

        public ProjectionRepository(IProjectionStore projectionStore, ISnapshotStore snapshotStore, ISnapshotStrategy snapshotStrategy, InMemoryProjectionVersionStore inMemoryVersionStore)
        {
            if (ReferenceEquals(null, projectionStore) == true) throw new ArgumentException(nameof(projectionStore));
            if (ReferenceEquals(null, snapshotStore) == true) throw new ArgumentException(nameof(snapshotStore));
            if (ReferenceEquals(null, snapshotStrategy) == true) throw new ArgumentException(nameof(snapshotStrategy));
            if (ReferenceEquals(null, inMemoryVersionStore) == true) throw new ArgumentException(nameof(inMemoryVersionStore));

            this.projectionStore = projectionStore;
            this.snapshotStore = snapshotStore;
            this.snapshotStrategy = snapshotStrategy;
            this.inMemoryVersionStore = inMemoryVersionStore;
        }

        ProjectionStream LoadProjectionStream(Type projectionType, ProjectionVersion version, IBlobId projectionId, SnapshotMeta snapshotMeta)
        {
            Func<ISnapshot> loadSnapshot = () => snapshotStore.Load(version.ProjectionName, projectionId, version);

            List<ProjectionCommit> projectionCommits = new List<ProjectionCommit>();
            int snapshotMarker = snapshotMeta.Revision;
            while (true)
            {
                snapshotMarker++;
                var loadedCommits = projectionStore.Load(version, projectionId, snapshotMarker).ToList();
                projectionCommits.AddRange(loadedCommits);

                if (projectionType.IsSnapshotable())
                {
                    if (snapshotStrategy.ShouldCreateSnapshot(projectionCommits, snapshotMeta.Revision))
                    {
                        ProjectionStream checkpointStream = new ProjectionStream(projectionId, projectionCommits, loadSnapshot);
                        var projectionState = checkpointStream.RestoreFromHistory(projectionType).Projection.State;
                        ISnapshot newSnapshot = new Snapshot(projectionId, version.ProjectionName, projectionState, snapshotMeta.Revision + 1);
                        snapshotStore.Save(newSnapshot, version);
                        loadSnapshot = () => newSnapshot;

                        projectionCommits.Clear();

                        log.Debug(() => $"Snapshot created for projection `{version.ProjectionName}` with id={projectionId} where ({loadedCommits.Count}) were zipped. Snapshot: `{newSnapshot.GetType().Name}`");
                    }
                }
                else
                    loadSnapshot = () => new NoSnapshot(projectionId, version.ProjectionName);

                if (loadedCommits.Count < snapshotStrategy.EventsInSnapshot)
                    break;
                else
                    log.Warn(() => $"Potential memory leak. The system will be down fairly soon. The projection `{version.ProjectionName}` with id={projectionId} loads a lot of projection commits ({loadedCommits.Count}) which puts a lot of CPU and RAM pressure. You can resolve this by configuring the snapshot settings`.");
            }

            ProjectionStream stream = new ProjectionStream(projectionId, projectionCommits, loadSnapshot);
            return stream;
        }

        /// <summary>
        /// You can remove this once c# 8 is released with Traits.
        /// </summary>
        ProjectionStream LoadProjectionStream(Type projectionType, ProjectionVersion version, IBlobId projectionId, ISnapshot snapshot)
        {
            Func<ISnapshot> loadSnapshot = () => snapshot;

            List<ProjectionCommit> projectionCommits = new List<ProjectionCommit>();
            int snapshotMarker = snapshot.Revision;
            while (true)
            {
                snapshotMarker++;
                var loadedCommits = projectionStore.Load(version, projectionId, snapshotMarker).ToList();
                projectionCommits.AddRange(loadedCommits);

                if (projectionType.IsSnapshotable() && snapshotStrategy.ShouldCreateSnapshot(projectionCommits, snapshot.Revision))
                {
                    ProjectionStream checkpointStream = new ProjectionStream(projectionId, projectionCommits, loadSnapshot);
                    var projectionState = checkpointStream.RestoreFromHistory(projectionType).Projection.State;
                    ISnapshot newSnapshot = new Snapshot(projectionId, version.ProjectionName, projectionState, snapshot.Revision + 1);
                    snapshotStore.Save(newSnapshot, version);
                    loadSnapshot = () => newSnapshot;

                    projectionCommits.Clear();

                    log.Debug(() => $"Snapshot created for projection `{version.ProjectionName}` with id={projectionId} where ({loadedCommits.Count}) were zipped. Snapshot: `{snapshot.GetType().Name}`");
                }

                if (loadedCommits.Count < snapshotStrategy.EventsInSnapshot)
                    break;
                else
                    log.Warn(() => $"Potential memory leak. The system will be down fairly soon. The projection `{version.ProjectionName}` with id={projectionId} loads a lot of projection commits ({loadedCommits.Count}) and snapshot `{snapshot.GetType().Name}` which puts a lot of CPU and RAM pressure. You can resolve this by configuring the snapshot settings`.");
            }

            ProjectionStream stream = new ProjectionStream(projectionId, projectionCommits, loadSnapshot);
            return stream;
        }

        public IProjectionGetResult<T> Get<T>(IBlobId projectionId) where T : IProjectionDefinition
        {
            if (ReferenceEquals(null, projectionId)) throw new ArgumentNullException(nameof(projectionId));

            Type projectionType = typeof(T);

            ProjectionStream stream = LoadProjectionStream(projectionType, projectionId);
            return stream.RestoreFromHistory<T>();
        }

        public IProjectionGetResult<IProjectionDefinition> Get(IBlobId projectionId, Type projectionType)
        {
            if (ReferenceEquals(null, projectionId)) throw new ArgumentNullException(nameof(projectionId));

            ProjectionStream stream = LoadProjectionStream(projectionType, projectionId);
            return stream.RestoreFromHistory(projectionType);
        }

        ProjectionStream LoadProjectionStream(Type projectionType, IBlobId projectionId)
        {
            string projectionName = projectionType.GetContractId();

            try
            {
                ProjectionVersion liveVersion = GetProjectionVersions(projectionName).GetLive();
                if (ReferenceEquals(null, liveVersion))
                {
                    log.Warn(() => $"Unable to find projection `live` version. ProjectionId:{projectionId} ProjectionName:{projectionName} ProjectionType:{projectionType.Name}");
                    return ProjectionStream.Empty();
                }

                ISnapshot snapshot = null;
                if (projectionType.IsSnapshotable())
                    snapshot = snapshotStore.Load(projectionName, projectionId, liveVersion);
                else
                    snapshot = new NoSnapshot(projectionId, projectionName);

                ProjectionStream stream = LoadProjectionStream(projectionType, liveVersion, projectionId, snapshot);

                return stream;
            }
            catch (Exception ex)
            {
                log.ErrorException($"Unable to load projection stream. ProjectionId:{projectionId} ProjectionName:{projectionName} ProjectionType:{projectionType.Name}", ex);
                return ProjectionStream.Empty();
            }
        }

        IProjectionGetResult<PersistentProjectionVersionHandler> GetProjectionVersionsFromStore(string contractId)
        {
            var versionId = new ProjectionVersionManagerId(contractId);
            var persistentVersionType = typeof(PersistentProjectionVersionHandler);
            var persistentVersionContractId = persistentVersionType.GetContractId();
            var persistentVersion = new ProjectionVersion(persistentVersionContractId, ProjectionStatus.Live, 1, persistentVersionType.GetProjectionHash());
            ProjectionStream stream = LoadProjectionStream(persistentVersionType, persistentVersion, versionId, new NoSnapshot(versionId, contractId).GetMeta());
            var queryResult = stream.RestoreFromHistory<PersistentProjectionVersionHandler>();
            return queryResult;
        }

        ProjectionVersions GetProjectionVersions(string projectionName)
        {
            try
            {
                if (string.IsNullOrEmpty(projectionName)) throw new ArgumentNullException(nameof(projectionName));

                var persistentVersionContractId = typeof(PersistentProjectionVersionHandler).GetContractId();
                if (string.Equals(persistentVersionContractId, projectionName, StringComparison.OrdinalIgnoreCase))
                    return GetPersistentProjectionVersions(persistentVersionContractId);

                ProjectionVersions versions = inMemoryVersionStore.Get(projectionName);
                if (versions == null || versions.Count == 0)
                {
                    var queryResult = GetProjectionVersionsFromStore(projectionName);
                    if (queryResult.Success)
                    {
                        if (queryResult.Projection.State.Live != null)
                            inMemoryVersionStore.Cache(queryResult.Projection.State.Live);
                        if (queryResult.Projection.State.Building != null)
                            inMemoryVersionStore.Cache(queryResult.Projection.State.Building);
                        versions = inMemoryVersionStore.Get(projectionName);
                    }

                    if (versions == null || versions.Count == 0)
                    {
                        var initialVersion = new ProjectionVersion(projectionName, ProjectionStatus.Building, 1, projectionName.GetTypeByContract().GetProjectionHash());
                        inMemoryVersionStore.Cache(initialVersion);
                        versions = inMemoryVersionStore.Get(projectionName);
                    }
                }

                return versions ?? new ProjectionVersions();
            }
            catch (Exception ex)
            {
                log.WarnException($"Unable to load projection versions. ProjectionName:{projectionName}", ex);
                return new ProjectionVersions();
            }
        }

        ProjectionVersions GetPersistentProjectionVersions(string projectionName)
        {
            if (string.IsNullOrEmpty(projectionName)) throw new ArgumentNullException(nameof(projectionName));

            ProjectionVersions versions = inMemoryVersionStore.Get(projectionName);
            if (versions == null || versions.Count == 0)
            {
                var queryResult = GetProjectionVersionsFromStore(projectionName);
                if (queryResult.Success)
                {
                    if (queryResult.Projection.State.Live != null)
                        inMemoryVersionStore.Cache(queryResult.Projection.State.Live);
                    if (queryResult.Projection.State.Building != null)
                        inMemoryVersionStore.Cache(queryResult.Projection.State.Building.WithStatus(ProjectionStatus.Live));
                    versions = inMemoryVersionStore.Get(projectionName);
                }

                // inception
                if (versions == null || versions.Count == 0)
                {
                    var initialVersion = new ProjectionVersion(projectionName, ProjectionStatus.Live, 1, typeof(PersistentProjectionVersionHandler).GetProjectionHash());

                    inMemoryVersionStore.Cache(initialVersion);
                    versions = inMemoryVersionStore.Get(projectionName);
                }
            }

            return versions ?? new ProjectionVersions();
        }

        public void Save(Type projectionType, IEvent @event, EventOrigin eventOrigin)
        {
            if (ReferenceEquals(null, projectionType)) throw new ArgumentNullException(nameof(projectionType));
            if (ReferenceEquals(null, @event)) throw new ArgumentNullException(nameof(@event));
            if (ReferenceEquals(null, eventOrigin)) throw new ArgumentNullException(nameof(eventOrigin));

            string projectionName = projectionType.GetContractId();
            var projection = FastActivator.CreateInstance(projectionType) as IProjectionDefinition;
            if (projection != null)
            {
                var projectionIds = projection.GetProjectionIds(@event);

                foreach (var projectionId in projectionIds)
                {
                    foreach (var version in GetProjectionVersions(projectionName))
                    {
                        SnapshotMeta snapshotMeta = snapshotStore.LoadMeta(projectionName, projectionId, version);
                        ProjectionStream projectionStream = LoadProjectionStream(projectionType, version, projectionId, snapshotMeta);
                        int snapshotMarker = snapshotStrategy.GetSnapshotMarker(projectionStream.Commits, snapshotMeta.Revision);

                        var commit = new ProjectionCommit(projectionId, version, @event, snapshotMarker, eventOrigin, DateTime.UtcNow);
                        projectionStore.Save(commit);
                    }
                }
            }
        }

        public void Save(Type projectionType, CronusMessage cronusMessage)
        {
            if (ReferenceEquals(null, projectionType)) throw new ArgumentNullException(nameof(projectionType));
            if (ReferenceEquals(null, cronusMessage)) throw new ArgumentNullException(nameof(cronusMessage));

            EventOrigin eventOrigin = cronusMessage.GetEventOrigin();
            IEvent @event = cronusMessage.Payload as IEvent;

            Save(projectionType, @event, eventOrigin);
        }
    }
}
