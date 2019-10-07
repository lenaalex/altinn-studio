using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;
using AltinnCore.Common.Configuration;
using AltinnCore.Common.Helpers;
using AltinnCore.Common.Services.Interfaces;
using AltinnCore.RepositoryClient.Model;
using AltinnCore.ServiceLibrary.Enums;
using AltinnCore.ServiceLibrary.Models;
using AltinnCore.ServiceLibrary.Services.Interfaces;
using Common.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceLibrary.Models;

namespace AltinnCore.Runtime.Controllers
{
    /// <summary>
    /// Handles party related operations
    /// </summary>
    [Authorize]
    [ApiController]
    public class PartiesController : ControllerBase
    {
        private readonly ILogger _logger;
        private readonly IAuthorization _authorization;
        private readonly UserHelper _userHelper;
        private readonly IApplication _application;
        private readonly IProfile _profile;
        private readonly GeneralSettings _settings;

        /// <summary>
        /// Initializes a new instance of the <see cref="PartiesController"/> class
        /// <param name="logger">The logger</param>
        /// <param name="authorization">the authorization service handler</param>
        /// <param name="profileService">the profile service</param>
        /// <param name="registerService">the register service</param>
        /// <param name="application">The repository service</param>
        /// <param name="settings">The general settings</param>
        /// </summary>
        public PartiesController(
                    ILogger<PartiesController> logger,
                    IAuthorization authorization,
                    IProfile profileService,
                    IRegister registerService,
                    IApplication application,
                    IOptions<GeneralSettings> settings)
        {
            _logger = logger;
            _authorization = authorization;
            _userHelper = new UserHelper(profileService, registerService, settings);
            _application = application;
            _profile = profileService;
            _settings = settings.Value;
        }

        /// <summary>
        /// Gets the list of parties the user can represent
        /// </summary>
        /// <param name="org">Unique identifier of the organisation responsible for the app.</param>
        /// <param name="app">Application identifier which is unique within an organisation.</param>
        /// <param name="allowedToInstantiateFilter">when set to true returns parties that are allowed to instantiate</param>
        /// <returns>parties</returns>
        [HttpGet("{org}/{app}/api/v1/parties")]
        public async Task<IActionResult> Get(string org, string app, bool allowedToInstantiateFilter = false)
        {
            UserContext userContext = _userHelper.GetUserContext(HttpContext).Result;
            List<Party> partyList = _authorization.GetPartyList(userContext.UserId);

            if (allowedToInstantiateFilter)
            {
                Application application = await _application.GetApplication(org, app);
                List<Party> validParties = InstantiationHelper.FilterPartiesByAllowedPartyTypes(partyList, application.PartyTypesAllowed);
                return Ok(validParties);
            }

            return Ok(partyList);
        }

        /// <summary>
        /// Validates party and profile settings before the end user is allowed to instantiate a new app instance
        /// </summary>
        /// <param name="org">Unique identifier of the organisation responsible for the app.</param>
        /// <param name="app">Application identifier which is unique within an organisation.</param>
        /// <param name="partyId">The selected partyId</param>
        /// <returns>A validation status</returns>
        [HttpPost("{org}/{app}/api/v1/parties/validateInstantiation")]
        public async Task<IActionResult> ValidateInstantiation(string org, string app, [FromQuery] int partyId)
        {
            UserContext userContext = _userHelper.GetUserContext(HttpContext).Result;
            UserProfile user = _profile.GetUserProfile(userContext.UserId).Result;
            List<Party> partyList = _authorization.GetPartyList(userContext.UserId);
            Application application = await _application.GetApplication(org, app);

            if (application == null)
            {
                return NotFound("Application not found");
            }

            PartyTypesAllowed partyTypesAllowed = application.PartyTypesAllowed;
            Party partyUserRepresents = null;
            List<Party> allowedPartiesTheUserCanRepresent = new List<Party>();

            // Check if the user can represent the supplied partyId
            if (partyId != user.PartyId)
            {
                Party represents = InstantiationHelper.GetPartyByPartyId(partyList, partyId);
                if (represents == null)
                {
                    // the user does not represent the chosen party id, is not allowed to initiate
                    return Ok(new InstantiationValidationResult
                    {
                        Valid = false,
                        Message = "The user does not represent the supplied party",
                        ValidParties = InstantiationHelper.FilterPartiesByAllowedPartyTypes(partyList, partyTypesAllowed)
                    });
                }

                partyUserRepresents = represents;
            }

            if (partyUserRepresents == null)
            {
                // if not set, the user represents itself
                partyUserRepresents = user.Party;
            }

            // Check if the application can be initiated with the party chosen
            bool canInstantiate = InstantiationHelper.IsPartyAllowedToInstantiate(partyUserRepresents, partyTypesAllowed);

            if (!canInstantiate)
            {
                return Ok(new InstantiationValidationResult
                {
                    Valid = false,
                    Message = "The supplied party is not allowed to instantiate the application",
                    ValidParties = InstantiationHelper.FilterPartiesByAllowedPartyTypes(partyList, partyTypesAllowed)
                });
            }

            return Ok(new InstantiationValidationResult
            {
                Valid = true,
            });
        }

        /// <summary>
        /// Updates the party the user represents
        /// </summary>
        /// <returns>Status code</returns>
        [HttpPut("{org}/{app}/api/v1/parties/{partyId}")]
        public async Task<IActionResult> UpdateSelectedParty(int partyId)
        {
            UserContext userContext = _userHelper.GetUserContext(HttpContext).Result;
            int userId = userContext.UserId;

            bool? isValid = await _authorization.ValidateSelectedParty(userId, partyId);

            if (!isValid.HasValue)
            {
                return StatusCode(500, "Something went wrong when trying to update selectedparty.");
            }
            else if (isValid.Value == false)
            {
                return BadRequest($"User {userId} cannot represent party {partyId}.");
            }

            Response.Cookies.Append(
            _settings.GetAltinnPartyCookieName,
            partyId.ToString(),
            new CookieOptions
            {
                Domain = _settings.HostName
            });

            return Ok("Party successfully updated");
        }
    }
}