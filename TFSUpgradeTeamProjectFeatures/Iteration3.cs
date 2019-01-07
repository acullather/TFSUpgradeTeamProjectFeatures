using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.Framework.Client;
using Microsoft.TeamFoundation.Framework.Server;
using Microsoft.TeamFoundation.Integration.Server;
using Microsoft.TeamFoundation.Server.Core;
using Microsoft.TeamFoundation.Server.WebAccess.WorkItemTracking.Common;
using Microsoft.TeamFoundation.VersionControl.Client;
using Microsoft.VisualStudio.Services.Common;

namespace TFSUpgradeTeamProjectFeatures
{
    static class Iteration3
    {
        static readonly string _tfsUrl = ConfigurationManager.AppSettings["TfsUrl"].ToString();
        static readonly string _tfsVersion = ConfigurationManager.AppSettings["TfsVersion"].ToString();

        public static void Process(string[] args)
        {
            string collectionName = "DefaultCollection";

            var server = TfsConfigurationServerFactory.GetConfigurationServer(new Uri(_tfsUrl));
            server.Authenticate();

            // Get the TF Request Context
            var collectionService = server.GetService<ITeamProjectCollectionService>();
            Console.WriteLine("Collections:");
            foreach (var teamProjectCollection in collectionService.GetCollections().Where(c => c.Name == collectionName))
            {
                Console.WriteLine(teamProjectCollection.Name);
                var collection = TfsTeamProjectCollectionFactory.GetTeamProjectCollection(new Uri($"{_tfsUrl}/{teamProjectCollection.Name}"));
                // Run the 'Configuration Features Wizard'
                using (var deploymentServiceHost = GetDeploymentServiceHost(collection.Uri.ToString(), out Guid instanceId))
                {
                    using (var context = GetContext(deploymentServiceHost, instanceId))
                    {
                        var vcs = collection.GetService<VersionControlServer>();
                        var projects = vcs.GetAllTeamProjects(true);
                        var commonStruct = collection.GetService<ICommonStructureService>();
                        foreach (TeamProject project in projects)
                        {
                            ProvisionProjectFeatures(context, project);
                        }
                    }
                }
            }

        }

        private static IVssDeploymentServiceHost GetDeploymentServiceHost(string urlToCollection, out Guid instanceId)
        {
            var vssCreds = new VssCredentials();
            using (var teamProjectCollection = new TfsTeamProjectCollection(new Uri(urlToCollection), vssCreds))
            {
                string connectionString = ConfigurationManager.AppSettings["ConfigDBConnectionString"].ToString();
                instanceId = teamProjectCollection.InstanceId;

                // Get the system context
                var deploymentHostProperties = new TeamFoundationServiceHostProperties
                {
                    HostType = TeamFoundationHostType.All
                };
                return DeploymentServiceHostFactory.CreateDeploymentServiceHost(deploymentHostProperties, SqlConnectionInfoFactory.Create(connectionString));
            }
        }

        private static IVssRequestContext GetContext(IVssDeploymentServiceHost deploymentServiceHost, Guid instanceId)
        {
            using (var deploymentRequestContext = deploymentServiceHost.CreateSystemContext())
            {
                // Get the identity for the tf request context
                var ims = deploymentRequestContext.GetService<TeamFoundationIdentityService>();
                var identity = ims.ReadRequestIdentity(deploymentRequestContext);

                // Get the tf request context
                TeamFoundationHostManagementService hostManagementService = deploymentRequestContext.GetService<TeamFoundationHostManagementService>();

                return hostManagementService.BeginUserRequest(deploymentRequestContext, instanceId, identity.Descriptor);
            }
        }

        private static void ProvisionProjectFeatures(IVssRequestContext context, TeamProject project)
        {
            // Get the Feature provisioning service ("Configure Features")
            var projectFeatureProvisioningService = context.GetService<ProjectFeatureProvisioningService>();
            var projectUri = project.ArtifactUri.AbsoluteUri;

            if (!projectFeatureProvisioningService.GetFeatures(context, projectUri).Where(f => (f.State == ProjectFeatureState.NotConfigured && !f.IsHidden)).Any())
            {
                // When the team project is already fully or partially configured, report it
                Console.WriteLine("{0}: Project is up to date.", project.Name);
            }
            else
            {
                // find the valid process templates
                var projectFeatureProvisioningDetails = projectFeatureProvisioningService.ValidateProcessTemplates(context, projectUri);

                int validProcessTemplateCount = projectFeatureProvisioningDetails.Where(d => d.IsValid).Count();

                if (validProcessTemplateCount == 0)
                {
                    // when there are no valid process templates found
                    Console.WriteLine("{0}: No valid process template found!");
                }
                else if (validProcessTemplateCount == 1)
                {
                    // at this point, only one process template without configuration errors is found
                    // configure the features for this team project
                    var projectFeatureProvisioningDetail = projectFeatureProvisioningDetails.ElementAt(0);
                    projectFeatureProvisioningService.ProvisionFeatures(context, projectUri, projectFeatureProvisioningDetail.ProcessTemplateDescriptorRowId);

                    Console.WriteLine("{0}: Configured using settings from {1}.", project.Name, projectFeatureProvisioningDetail.ProcessTemplateDescriptorName);
                }
                else if (validProcessTemplateCount > 1)
                {
                    // when multiple process templates found that closely match, report it
                    Console.WriteLine("{0}: Multiple valid process templates found!", project.Name);
                }
            }
        }
    }
}