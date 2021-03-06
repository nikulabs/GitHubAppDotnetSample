using System;
using System.Collections.Immutable;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using SAXGitHubApp.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebHooks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Octokit;

namespace SAXGitHubApp.Controllers
{
    // this class is taken from the webhook sample in https://github.com/aspnet/AspLabs
    public class GitHubController : ControllerBase
    {

        [GitHubWebHook(EventName = "push", Id = "It")]
        public IActionResult HandlerForItsPushes(string[] events, JObject data)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            return Ok();
        }

        [GitHubWebHook(Id = "It")]
        public IActionResult HandlerForIt(string[] events, JObject data)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            return Ok();
        }

        [GitHubWebHook(EventName = "issues")]
        public async System.Threading.Tasks.Task<IActionResult> HandlerForPushAsync(string id, JObject data)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // get the values from the payload data
            var issueNumber = (int)data["issue"]["number"];
            var installationId = (int)data["installation"]["id"];
            var owner = (string)data["repository"]["owner"]["login"];
            var repo = (string)data["repository"]["name"];

            // retrieve the client generated by the CreateGitHubClientFilter
            var appClient = (GitHubClient)ControllerContext.RouteData.Values[Constants.GitHubClient];

            if (appClient == null)
            {
                return Unauthorized();
            }

            // create an installation token en ensure we respond to the right GitHubApp installation
            var response = await appClient.GitHubApps.CreateInstallationToken(installationId);

            // create a client with the installation token
            var installationClient = new GitHubClient(new ProductHeaderValue($"{Constants.GitHubAppName}_{installationId}"))
            {
                Credentials = new Credentials(response.Token)
            };
            var configFile = await installationClient.Repository.Content.GetAllContents(
                 owner, repo, Constants.ConfigFile
            );

            var configContent = configFile[0].Content;
            var appConfig = JsonConvert.DeserializeObject<IEnumerable<RuleItem>>(configContent);

            // add a comment to the issue
            var issueComment = await installationClient.Issue.Comment.Create(owner, repo, issueNumber, JsonConvert.SerializeObject(appConfig));
            return Ok();
        }


        [GitHubWebHook(EventName = "pull_request")]
        public async System.Threading.Tasks.Task<IActionResult> HandlerForPullRequest(string id, JObject data)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // get the values from the payload data
            // fields needed for token
            var installationId = (int)data["installation"]["id"];
            var owner = (string)data["repository"]["owner"]["login"];
            var repo = (string)data["repository"]["name"];
            var shaRef = (string)data["pull_request"]["head"]["sha"];

            //fields for pull request
            var issueNumber = (int)data["number"];
            // retrieve the client generated by the CreateGitHubClientFilter
            var appClient = (GitHubClient)ControllerContext.RouteData.Values[Constants.GitHubClient];

            if (appClient == null)
            {
                return Unauthorized();
            }

            // create an installation token en ensure we respond to the right GitHubApp installation
            var response = await appClient.GitHubApps.CreateInstallationToken(installationId);

            // create a client with the installation token
            var installationClient = new GitHubClient(new ProductHeaderValue($"{Constants.GitHubAppName}_{installationId}"))
            {
                Credentials = new Credentials(response.Token)
            };
            var configFile = await installationClient.Repository.Content.GetAllContentsByRef(
                 owner, repo, Constants.ConfigFile, shaRef
            );
            var filesChanged = await installationClient.Repository.PullRequest.Files(owner, repo, issueNumber);
            var fileNamesChanged = filesChanged.Select(x => x.FileName).ToImmutableList();

            var configContent = configFile[0].Content;
            var appConfig = JsonConvert.DeserializeObject<IEnumerable<RuleItem>>(configContent);
            var applicableRules = new Dictionary<RuleItem, List<string>>();
            foreach (RuleItem rule in appConfig)
            {
                var rgx = new Regex(rule.Pattern);
                foreach(var fileName in fileNamesChanged)
                {
                    if (rgx.IsMatch(fileName))
                    {
                        if (applicableRules.ContainsKey(rule))
                        {
                            applicableRules[rule].Append(fileName);
                        }
                        applicableRules[rule] = new List<string>{fileName};
                    }
                }

            }

            if (applicableRules.Count() > 0)
            {
                var commentBuilder = new StringBuilder();
                foreach (var rule in applicableRules)
                {
                    commentBuilder.AppendLine($"{rule.Key.Name}: {rule.Key.Message}");
                    commentBuilder.Append(String.Join("\n\t", rule.Value));
                }

                // add a comment to the pull reques
                var prComment = await installationClient.Issue.Comment.Create(owner, repo, issueNumber, commentBuilder.ToString());
            }

            return Ok();
        }

        [GitHubWebHook]
        public IActionResult GitHubHandler(string id, string @event, JObject data)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            return Ok();
        }

        [GeneralWebHook]
        public IActionResult FallbackHandler(string receiverName, string id, string eventName)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            return Ok();
        }
    }
}