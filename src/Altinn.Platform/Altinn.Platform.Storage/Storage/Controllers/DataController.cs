using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;

namespace Altinn.Platform.Storage.Controllers
{
    /// <summary>
    /// api for managing the form data element
    /// </summary>
    [Route("storage/api/v1/instances/{instanceId:guid}/data")]
    [ApiController]
    public class DataController : Controller
    {
        private static readonly FormOptions _defaultFormOptions = new FormOptions();
        private readonly string prefix = "storage/api/v1";
        private readonly IDataRepository _dataRepository;
        private readonly IInstanceRepository _instanceRepository;
        private readonly IApplicationRepository _applicationRepository;

        /// <summary>
        /// Initializes a new instance of the <see cref="DataController"/> class
        /// </summary>
        /// <param name="formRepository">the form data repository handler</param>
        /// <param name="instanceRepository">the repository</param>
        /// <param name="applicationRepository">the application repository</param>
        public DataController(IDataRepository formRepository, IInstanceRepository instanceRepository, IApplicationRepository applicationRepository)
        {
            _dataRepository = formRepository;
            _instanceRepository = instanceRepository;
            _applicationRepository = applicationRepository;
        }

        /// <summary>
        /// Deletes a data element.
        /// </summary>
        /// <param name="instanceId">the instance owning the data element</param>
        /// <param name="dataId">the instance of the data element</param>
        /// <param name="instanceOwnerId">the owner of the instance</param>
        /// <returns></returns>
        [HttpDelete("{dataId:guid}")]
        public async Task<IActionResult> Delete(Guid instanceId, Guid dataId, int instanceOwnerId)
        {
            // check if instance id exist and user is allowed to change the instance data            
            Instance instance = await _instanceRepository.GetOneAsync(instanceId, instanceOwnerId);
            if (instance == null)
            {
                return NotFound("Provided instanceId is unknown to storage service");
            }

            string dataIdString = dataId.ToString();

            if (instance.Data.Exists(m => m.Id == dataIdString))
            {
                string storageFileName = DataFileName(instance.ApplicationId, instanceId.ToString(), dataId.ToString());

                bool result = await _dataRepository.DeleteDataInStorage(storageFileName);

                if (result)
                {
                    // Update instance record
                    Data data = instance.Data.Find(m => m.Id == dataIdString);
                    instance.Data.Remove(data);
                    Instance storedInstance = await _instanceRepository.UpdateInstanceInCollectionAsync(instanceId, instance);

                    return Ok(storedInstance);
                }
            }

            return BadRequest();
        }

        /// <summary>
        /// Save the form data
        /// </summary>
        /// <param name="instanceOwnerId">the instance owner id (an integer)</param>
        /// <param name="instanceId">the instanceId</param>
        /// <param name="dataId">the data id</param>
        /// <returns>The data file as an asyncronous streame</returns>        
        /// <returns>If the request was successful or not</returns>
        // GET /instances/{instanceId}/data/{dataId}
        [HttpGet("{dataId:guid}")]
        public async Task<IActionResult> Get(int instanceOwnerId, Guid instanceId, Guid dataId)
        {
            if (instanceOwnerId == 0 || instanceId == null || dataId == null)
            {
                return BadRequest("Missing parameter values: neither of instanceId, dataId or instanceOwnerId can be empty");
            }

            // check if instance id exist and user is allowed to change the instance data            
            Instance instance = await _instanceRepository.GetOneAsync(instanceId, instanceOwnerId);
            if (instance == null)
            {
                return NotFound("Provided instanceId and instanceOwnerId is unknown to platform storage service");
            }

            string storageFileName = DataFileName(instance.ApplicationId, instanceId.ToString(), dataId.ToString());
            string dataIdString = dataId.ToString();

            // check if dataId exists in instance
            if (instance.Data.Exists(m => m.Id == dataIdString))
            {
                Data data = instance.Data.Find(m => m.Id == dataIdString);

                if (string.Equals(data.StorageUrl, storageFileName))
                {
                    Stream dataStream = await _dataRepository.GetDataInStorage(storageFileName);

                    if (dataStream == null)
                    {
                        return NotFound("Unable to read data storage for " + dataIdString);
                    }

                    return File(dataStream, data.ContentType, data.FileName);
                }
            }

            return NotFound("Unable to find requested data item");
        }

        /// <summary>
        /// Save the form data
        /// </summary>
        /// <param name="instanceOwnerId">the instance owner id (an integer)</param>
        /// <param name="instanceId">the instanceId</param>
        /// <returns>The data file as an asyncronous streame</returns>        
        /// <returns>If the request was successful or not</returns>
        // GET /instances/{instanceId}/data/{dataId}
        [HttpGet]
        public async Task<IActionResult> GetMany(int instanceOwnerId, Guid instanceId)
        {
            if (instanceOwnerId == 0 || instanceId == null)
            {
                return BadRequest("Missing parameter values: neither of instanceId, instanceOwnerId can be empty");
            }

            // check if instance id exist and user is allowed to change the instance data            
            Instance instance = await _instanceRepository.GetOneAsync(instanceId, instanceOwnerId);
            if (instance == null)
            {
                return NotFound("Provided instanceId and instanceOwnerId is unknown to platform storage service");
            }

            List<Data> dataList = new List<Data>();
            foreach (Data data in instance.Data)
            {
                if (data.FormId != "default")
                {
                    dataList.Add(data);
                }
            }

            return Ok(dataList);
        }

        /// <summary>
        /// Formats a filename for blob storage.
        /// </summary>
        /// <param name="applicationId">the application id</param>
        /// <param name="instanceId">the instance id</param>
        /// <param name="dataId">the data id</param>
        /// <returns></returns>
        public static string DataFileName(string applicationId, string instanceId, string dataId)
        {
            return $"{applicationId}/{instanceId}/data/{dataId}";
        }

        /// <summary>
        /// Create and save the form data
        /// </summary>
        /// <param name="instanceOwnerId">instance owner id</param>
        /// <param name="instanceId">the instance to update</param>
        /// <param name="formId">the formId to upload data for</param>
        /// <returns>If the request was successful or not</returns>
        // POST /instances/{instanceId}/data?formId={formId}&instanceOwnerId={instanceOwnerId}      
        [HttpPost]
        [DisableFormValueModelBinding]
        public async Task<IActionResult> CreateAndUploadData(int instanceOwnerId, Guid instanceId, string formId)
        {
            if (instanceOwnerId == 0 || instanceId == null || string.IsNullOrEmpty(formId) || Request.Body == null)
            {
                return BadRequest("Missing parameter values: instanceOwnerId, instanceId, formId or file content cannot be null");
            }

            // check if instance id exist and user is allowed to change the instance data            
            Instance instance = await _instanceRepository.GetOneAsync(instanceId, instanceOwnerId);
            if (instance == null)
            {
                return NotFound("Provided instanceId is unknown to platform storage service");
            }

            // check metadata
            ApplicationMetadata appInfo = GetApplicationInformation(instance.ApplicationId);
            if (appInfo == null || !appInfo.Forms.Exists(f => f.Id.Equals(formId)))
            {
                if (appInfo == null)
                {
                    return Forbid("Application Metadata is not registered for this applicationId");
                }
                
                return Forbid("Application metadata has not registered a form definition with this formId");
            }

            DateTime creationTime = DateTime.UtcNow;

            Stream theStream = null;
            string contentType = null;
            string contentFileName = null;
            long fileSize = 0;

            if (MultipartRequestHelper.IsMultipartContentType(Request.ContentType))
            {
                // Only read the first section of the mulitpart message.
                MediaTypeHeaderValue mediaType = MediaTypeHeaderValue.Parse(Request.ContentType);
                string boundary = MultipartRequestHelper.GetBoundary(mediaType, _defaultFormOptions.MultipartBoundaryLengthLimit);

                MultipartReader reader = new MultipartReader(boundary, Request.Body);
                MultipartSection section = await reader.ReadNextSectionAsync();

                theStream = section.Body;
                contentType = section.ContentType;

                bool hasContentDisposition = ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out ContentDispositionHeaderValue contentDisposition);

                if (hasContentDisposition)
                {
                    contentFileName = contentDisposition.FileName.ToString();
                    fileSize = contentDisposition.Size ?? 0;
                }
            }
            else
            {
                theStream = Request.Body;
                contentType = Request.ContentType;
            }

            if (theStream == null)
            {
                return BadRequest("No data attachements found");
            }

            string dataId = Guid.NewGuid().ToString();

            string dataLink = $"{prefix}/instances/{instanceId}/data/{dataId}";

            // create new data element, store data in blob
            Data newData = new Data
            {
                // update data record
                Id = dataId,
                FormId = formId,
                ContentType = contentType,
                CreatedBy = User.Identity.Name,
                CreatedDateTime = creationTime,
                FileName = contentFileName ?? $"{dataId}.xml",
                LastChangedBy = User.Identity.Name,
                LastChangedDateTime = creationTime,
                Link = dataLink,
                FileSize = fileSize
            };

            string filePath = DataFileName(instance.ApplicationId, instanceId.ToString(), newData.Id.ToString());
            newData.StorageUrl = filePath;

            if (instance.Data == null)
            {
                instance.Data = new List<Data>();
            }

            instance.Data.Add(newData);

            try
            {
                // store file as blob
                await _dataRepository.CreateDataInStorage(theStream, filePath);
            }
            catch (Exception e)
            {
                return StatusCode(500, $"Unable to create instance data in storage: {e}");
            }

            try
            {
                // update instance
                Instance result = await _instanceRepository.UpdateInstanceInCollectionAsync(instanceId, instance);

                return Ok(result);
            }
            catch (Exception e)
            {
                return StatusCode(500, $"Unable to store instance in storage: {e}");
            }            
        }

        /// <summary>
        /// Update and save data
        /// </summary>
        /// <param name="instanceOwnerId">instance owner id</param>
        /// <param name="instanceId">the instance to update</param>
        /// <param name="dataId">the dataId to upload data to</param>
        /// <returns>If the request was successful or not</returns>
        // PUT /instances/{instanceId}/data/{dataId}?instanceOwnerId=2339      
        [HttpPut("{dataId}")]
        [DisableFormValueModelBinding]
        public async Task<IActionResult> OverwriteData(int instanceOwnerId, Guid instanceId, Guid dataId)
        {
            if (instanceOwnerId == 0 || dataId == null || instanceId == null || Request.Body == null)
            {
                return BadRequest("Missing parameter values: instanceOwnerId, instanceId, datafile or attached file content cannot be empty");
            }

            // check if instance id exist and user is allowed to change the instance data            
            Instance instance = await _instanceRepository.GetOneAsync(instanceId, instanceOwnerId);
            if (instance == null)
            {
                return NotFound("Provided instanceId is unknown to platform storage service");
            }

            string dataIdString = dataId.ToString();

            // check that data element exists, if not return not found
            if (instance.Data != null && instance.Data.Exists(m => m.Id == dataIdString))
            {
                Data data = instance.Data.Find(m => m.Id == dataIdString);

                if (data == null)
                {
                    return NotFound("Dataid is not not seen before, try create a new data element");
                }

                string storageFileName = DataFileName(instance.ApplicationId.ToString(), instanceId.ToString(), dataIdString);

                if (string.Equals(data.StorageUrl, storageFileName))
                {
                    DateTime updateTime = DateTime.UtcNow;

                    Stream theStream = null;
                    string contentType = null;
                    string contentFileName = null;

                    if (MultipartRequestHelper.IsMultipartContentType(Request.ContentType))
                    {
                        // Only read the first section of the mulitpart message.
                        MediaTypeHeaderValue mediaType = MediaTypeHeaderValue.Parse(Request.ContentType);
                        string boundary = MultipartRequestHelper.GetBoundary(mediaType, _defaultFormOptions.MultipartBoundaryLengthLimit);

                        MultipartReader reader = new MultipartReader(boundary, Request.Body);
                        MultipartSection section = await reader.ReadNextSectionAsync();

                        theStream = section.Body;
                        contentType = section.ContentType;

                        bool hasContentDisposition = ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out ContentDispositionHeaderValue contentDisposition);

                        if (hasContentDisposition)
                        {
                            contentFileName = contentDisposition.FileName.ToString();
                        }
                    }
                    else
                    {
                        theStream = Request.Body;
                        contentType = Request.ContentType;
                    }

                    if (theStream == null)
                    {
                        return BadRequest("No data attachements found");
                    }

                    DateTime changedTime = DateTime.UtcNow;

                    // update data record
                    data.ContentType = contentType;
                    data.FileName = contentFileName ?? data.FileName;
                    data.LastChangedBy = User.Identity.Name;
                    data.LastChangedDateTime = changedTime;

                    instance.LastChangedDateTime = changedTime;
                    instance.LastChangedBy = User.Identity.Name;

                    // store file as blob                      
                    bool success = await _dataRepository.UpdateDataInStorage(theStream, storageFileName);

                    if (success)
                    {
                        // update instance
                        Instance result = await _instanceRepository.UpdateInstanceInCollectionAsync(instanceId, instance);

                        return Ok(result);
                    }

                    return UnprocessableEntity();                
                }
            }

            return UnprocessableEntity();
        }        

        private ApplicationMetadata GetApplicationInformation(string applicationId)
        {
            string applicationOwnerId = ApplicationHelper.GetApplicationOwner(applicationId);

            ApplicationMetadata application = _applicationRepository.FindOne(applicationId, applicationOwnerId).Result;

            return application;            
        }
    }
}