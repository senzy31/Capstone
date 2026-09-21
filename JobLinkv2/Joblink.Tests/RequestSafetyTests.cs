using System.Reflection;
using Joblink.Services.Accounts;
using JobLinkv2.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace Joblink.Tests
{
    // Tripwires. If someone later adds a plan / premium_until / role column and
    // exposes it through a request class or binds a database model straight
    // from a request body, one of these fails and says why - before a user can
    // grant themselves a subscription.
    public class RequestSafetyTests
    {
        private static readonly Assembly Web = typeof(Program).Assembly;
        private static readonly Assembly Data = typeof(UserModel).Assembly;

        // Words a client may never be able to set on a request.
        private static readonly string[] NeverFromAClient =
        {
            "plan", "premium", "subscription", "tier", "passwordhash", "isdeleted", "userid", "createdat", "role"
        };

        // The few places a request legitimately carries one of those words.
        private static readonly HashSet<string> Allowed = new()
        {
            "SignupRequest.Role"    // limited to job seeker / employer by UserAccountService
        };

        // Where a password is expected: sign up, log in, and confirming an email change.
        private static readonly HashSet<string> PasswordFields = new()
        {
            "SignupRequest.Password", "LoginRequest.Password", "UpdateAccountRequest.CurrentPassword"
        };

        private static IEnumerable<Type> RequestClasses() =>
            Web.GetTypes().Concat(Data.GetTypes())
               .Where(t => t.IsClass && t.IsPublic && t.Name.EndsWith("Request", StringComparison.Ordinal));

        [Fact]
        public void Request_classes_cannot_carry_privileged_fields()
        {
            var problems = new List<string>();

            foreach (var type in RequestClasses())
            {
                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    var name = $"{type.Name}.{property.Name}";
                    var lower = property.Name.ToLowerInvariant();

                    if (Allowed.Contains(name))
                        continue;

                    if (NeverFromAClient.Any(lower.Contains))
                        problems.Add($"{name} is a field a client must never set.");

                    if (lower.Contains("password") && !PasswordFields.Contains(name))
                        problems.Add($"{name} takes a password that isn't one of the expected places.");
                }
            }

            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        }

        [Fact]
        public void There_are_request_classes_to_check()
        {
            // guards the test above against silently checking nothing
            Assert.Contains(RequestClasses(), t => t == typeof(SignupRequest));
            Assert.Contains(RequestClasses(), t => t == typeof(UpdateAccountRequest));
            Assert.Contains(RequestClasses(), t => t.Name == "LoginRequest");
        }

        // Adding a field to these two is a security decision, so it should mean
        // editing this test on purpose.
        [Fact]
        public void The_account_requests_carry_exactly_these_fields()
        {
            static string[] Fields(Type t) =>
                t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

            Assert.Equal(new[] { "CompanyName", "Email", "FullName", "Password", "Role" }, Fields(typeof(SignupRequest)));
            Assert.Equal(new[] { "CompanyName", "CurrentPassword", "Email", "FullName" }, Fields(typeof(UpdateAccountRequest)));
        }

        [Fact]
        public void The_account_response_has_no_password_hash()
        {
            var names = typeof(AccountResponse).GetProperties().Select(p => p.Name.ToLowerInvariant());

            Assert.DoesNotContain(names, n => n.Contains("password") || n.Contains("hash"));
        }

        // Controllers that still bind a database model straight from the request
        // body. Each one is reduced to the allowed fields before it is saved (and
        // has its own tests); the list shrinks as they move to request classes.
        private static readonly HashSet<string> StillBindingModels = new()
        {
            "ApplicationController",
            "JoblistingController",
            "JobMatchController",
            "NotificationController",
            "SavedJobsController",
            "SkillsController"
        };

        [Fact]
        public void Endpoints_do_not_bind_database_models_from_the_request()
        {
            var problems = new List<string>();

            var controllers = Web.GetTypes().Where(t => t.IsClass && !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t));

            foreach (var controller in controllers)
            {
                if (StillBindingModels.Contains(controller.Name))
                    continue;

                foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (action.GetCustomAttributes<HttpMethodAttribute>().Any() == false)
                        continue;

                    foreach (var parameter in action.GetParameters().Where(p => IsDatabaseModel(p.ParameterType)))
                        problems.Add($"{controller.Name}.{action.Name}({parameter.Name}) binds {parameter.ParameterType.Name} straight from the request.");
                }
            }

            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        }

        [Fact]
        public void The_list_of_controllers_still_binding_models_has_no_stale_entries()
        {
            var actual = Web.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t))
                .Where(c => c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
                    .SelectMany(m => m.GetParameters())
                    .Any(p => IsDatabaseModel(p.ParameterType)))
                .Select(c => c.Name)
                .ToHashSet();

            // anything listed as "still binding" must really still bind a model,
            // so converting a controller forces its removal from the list
            var stale = StillBindingModels.Where(name => !actual.Contains(name)).ToList();

            Assert.True(stale.Count == 0, "No longer binds a model - remove from StillBindingModels: " + string.Join(", ", stale));
        }

        private static bool IsDatabaseModel(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;

            if (type.IsArray)
                type = type.GetElementType()!;

            if (type.IsGenericType)
                return type.GetGenericArguments().Any(IsDatabaseModel);

            return type.Assembly == Data && type.Namespace == "JobLinkv2.Models";
        }
    }
}
