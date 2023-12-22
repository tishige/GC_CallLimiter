using DocumentFormat.OpenXml.Drawing.Charts;
using DocumentFormat.OpenXml.Office2010.Excel;
using DocumentFormat.OpenXml.Vml.Office;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using NodaTime;
using Radzen;
using StackExchange.Redis;
using System.Linq.Dynamic.Core;
using System.Net.Http.Headers;
using System.Text;

namespace CallLimiterWeb.Data
{
	public interface IDNISService
    {
        Task<List<LimitSettingsDTO>> GetAllSettingsAsync();
        Task<LimitSettingsDTO> GetDNISSettingsByIdAsync(string limitSettingsId);
        Task<string> PostDNISSettings(LimitSettingsDTO entity);
        Task<string> PutDNISSettings(string limitSettingsId, LimitSettingsDTO entity);
        Task<string> DeleteDNISSettings(string limitSettingsId);
        Task<string> FetchTokenFromCode(string code);
        Task<JObject> FetchUserInfo(string accessToken);
		Task<JObject> FetchUserOrgInfo(string accessToken);
        Task<List<UserDivision>> FetchUserDivision(string code);
        Task<List<QueueListDTO>> FetchQueueList();
        Task<string> GetSystemSettingsREDIS();
        Task<string> PostSystemSettingsREDIS(string setting);
        Task<List<AccDTO>> GetAccAsync(DateTime? start, DateTime? end, string? userDivisionsId = null);

		Task<List<ANIList>> GetANISettingsAsync();
		Task<string> PostANISettings(string ani);
		Task<string> DeleteANISettings(string ani);

		Task<string> RecoveryREDIS(string? setting);

		Task<ExportResult> ExportAccToExcel(DateTime? startdate = null, DateTime? enddate = null, Query? query = null);
	}


    public class DNISService : IDNISService
    {
        // mariaDB
        private readonly IDbContextFactory<DataContext> _contextFactory;
        // Redis
		private readonly IConnectionMultiplexer _redisCache;
        private readonly IDatabaseAsync _redisDBAsync;

        // Genesys Cloud
		private readonly IConfiguration _configuration;
        private string? _environment;
        private string?	_clientId;
        private string? _clientSecret;
        private string? _redirectUri;
		private string? _orgname;
		private int _maxAccExportRecords;

		private readonly IHttpContextAccessor _httpContextAccessor;

		private readonly NavigationManager _navigationManager;

		private readonly HttpClient _httpClient = new HttpClient();

        private readonly ILogger<DNISService> _logger;


		public DNISService(IDbContextFactory<DataContext> contextFactory, IHttpContextAccessor httpContextAccessor, IConnectionMultiplexer redis, IConfiguration configuration, NavigationManager navigationManager, ILogger<DNISService> logger)
        {
            _contextFactory = contextFactory;
            _httpContextAccessor = httpContextAccessor;
			_redisCache = redis;
            _redisDBAsync = _redisCache.GetDatabase();
			_configuration = configuration;

			_environment = _configuration["GenesysCloud:Environment"];
			_clientId = _configuration["GenesysCloud:ClientId"];
			_clientSecret = _configuration["GenesysCloud:ClientSecret"];
			_redirectUri = _configuration["GenesysCloud:RedirectUri"];
			_orgname = _configuration["GenesysCloud:OrganizationShortName"];

			int tempRecords;
			if (Int32.TryParse(_configuration["CLM:MaxAccExportRecords"], out tempRecords))
			{
				_maxAccExportRecords = tempRecords;
			}
			else
			{
				_maxAccExportRecords = 10000;
			}

			_navigationManager = navigationManager;

			_logger = logger;

        }

        public async Task<string> GetSystemSettingsREDIS()
        {
			_logger.LogInformation("CLM:GetSystemSettingsREDIS");

			try
			{
				string? value = await _redisDBAsync.StringGetAsync("systemSettings");
				if (String.IsNullOrEmpty(value))
				{
					await PostSystemSettingsREDIS("Unlimited");
					value = "Unlimited";
					_logger.LogInformation("CLM:systemSettings was not found. Set Unlimited");
				}

				_logger.LogInformation($"CLM:GetSystemSettingsREDIS done Set:{value}");

				return value;
			}
			catch (Exception e)
			{
				_logger.LogError("CLM:GetSystemSettings" + e);

				throw;
			}

		}

        public async Task<string> PostSystemSettingsREDIS(string setting)
        {
            bool result = await _redisDBAsync.StringSetAsync("systemSettings", setting);
            string value = result ? "Success" : "Failed";

            _logger.LogInformation($"CLM:PostsystemSettingsREDIS systemSettings:{setting} result:{result}");

            return value;
        }



        public async Task<List<LimitSettingsDTO>> GetAllSettingsAsync()
        {
			_logger.LogInformation("CLM:GetAllSettingsAsync");
			string? userName = _httpContextAccessor.HttpContext!.Session.GetString("userName");
            string? eMail = _httpContextAccessor.HttpContext!.Session.GetString("email");
            string? gcCode = _httpContextAccessor.HttpContext!.Session.GetString("GCCode");
            string? userDivisionsName = _httpContextAccessor.HttpContext!.Session.GetString("userDivisionsName");
            string? userDivisionsId = _httpContextAccessor.HttpContext!.Session.GetString("userDivisionsId")!;

			if (userDivisionsId == null)
			{
				_logger.LogError("CLM:Error in GetAllSettingsAsync userDivisionsId is null");
                return new List<LimitSettingsDTO>();
            }


			string[] divisionIds = userDivisionsId.Split('|');

            try
			{
				using (var context = _contextFactory.CreateDbContext())
				{
					var limitSettings = await context.limitSettings
					.Where(ls => divisionIds.Contains(ls.DivisionId))
					.Include(ls => ls.DNISListLMS)
					.Include(ls => ls.QueueListLMS)
					.ToListAsync();

					_logger.LogInformation("CLM:GetAllSettingsAsync success");

					return limitSettings.Select(ls => new LimitSettingsDTO
					{
						LimitSettingsId = ls.LimitSettingsId,
						Description = ls.Description,
						DNIS = ls.DNIS,
						IsAllBusy = ls.IsAllBusy,
						Type = ls.Type,
						MaxLimitValue = ls.MaxLimitValue,
						AvailableAgentsLimitValue = ls.AvailableAgentsLimitValue,
						Conditions = ls.Conditions,

						DNISList = ls.DNISListLMS!.Select(dnis => new DNISListDTO
						{
							DNIS = dnis.DNIS,
							LimitSettingsId = dnis.LimitSettingsId
						}).ToList(),
						QueueList = ls.QueueListLMS!.Select(queue => new QueueListDTO
						{
							QueueName = queue.QueueName,
							QueueId = queue.QueueId
						}).ToList(),
						IsScheduled = ls.IsScheduled,
						ScheduleStart = ls.ScheduleStart,
						ScheduleEnd = ls.ScheduleEnd,
						IsEnabled = ls.IsEnabled,
						IsSettingsError = ls.IsSettingsError,
						DateCreated = ls.DateCreated,
						DateModified = ls.DateModified,
						DivisionId = ls.DivisionId,
						DivisionName = ls.DivisionName,

					}).ToList();

				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex.Message, "CLM:Error in GetAllSettingsAsync");

				throw;
			}

        }

        public async Task<LimitSettingsDTO> GetDNISSettingsByIdAsync(string limitSettingsId)
        {
			_logger.LogInformation($"CLM:GetDNISSettingsByIdAsync limitSettingsId:{limitSettingsId}");

			try
			{
				using (var context = _contextFactory.CreateDbContext())
				{
					var limitSetting = await context.limitSettings
						.Include(ls => ls.DNISListLMS)
						.Include(ls => ls.QueueListLMS)
						.FirstOrDefaultAsync(ls => ls.LimitSettingsId == limitSettingsId);

					if (limitSetting != null)
					{
						_logger.LogInformation($"CLM:GetDNISSettingsByIdAsync success");

						return new LimitSettingsDTO
						{
							LimitSettingsId = limitSetting.LimitSettingsId,
							Description = limitSetting.Description,
							DNIS = limitSetting.DNIS,
							IsAllBusy = limitSetting.IsAllBusy,
							Type = limitSetting.Type,
							MaxLimitValue = limitSetting.MaxLimitValue,
							AvailableAgentsLimitValue = limitSetting.AvailableAgentsLimitValue,
							Conditions = limitSetting.Conditions,
							DNISList = limitSetting.DNISListLMS!.Select(dnis => new DNISListDTO
							{
								DNIS = dnis.DNIS,
								LimitSettingsId = dnis.LimitSettingsId
							}).ToList(),
							QueueList = limitSetting.QueueListLMS!.Select(queue => new QueueListDTO
							{
								QueueName = queue.QueueName,
								QueueId = queue.QueueId
							}).ToList(),
							IsScheduled = limitSetting.IsScheduled,
							ScheduleStart = limitSetting.ScheduleStart,
							ScheduleEnd = limitSetting.ScheduleEnd,
							IsEnabled = limitSetting.IsEnabled,
							IsSettingsError = limitSetting.IsSettingsError,
							DateCreated = limitSetting.DateCreated,
							DateModified = limitSetting.DateModified,
							DivisionId = limitSetting.DivisionId,
							DivisionName = limitSetting.DivisionName,
							CreatedBy = limitSetting.CreatedBy,
							LastModifiedBy = limitSetting.LastModifiedBy
						};
					}
					else
					{
						_logger.LogError($"CLM:GetDNISSettingsByIdAsync limitSetting was null");

						return null!;
					}
				}

			}
			catch (Exception ex)
			{
				_logger.LogError(ex.Message, "CLM:Error in GetDNISSettingsByIdAsync");

				throw;
			}

        }

        public async Task<string> PostDNISSettings(LimitSettingsDTO entityDTO)
        {
			_logger.LogInformation($"CLM:PostDNISSettings");
			try
			{
				using (var context = _contextFactory.CreateDbContext())
				{
					var divIDinExistingList = await context.limitSettings.Where(x => x.DNIS == entityDTO.DNIS && x.LimitSettingsId != entityDTO.LimitSettingsId).ToListAsync();
					if(divIDinExistingList.Count > 0 && divIDinExistingList.Select(x=>x.DivisionId).FirstOrDefault() != entityDTO.DivisionId)
					{
						string? divName = divIDinExistingList.Select(x => x.DivisionName).FirstOrDefault();
						_logger.LogError($"CLM:PostDNISSettings The specified DNIS is already in use in the division '{divName}'");
						return $"The specified DNIS is already in use in the division '{divName}'";
					}

					//Check if the enabled limitSettings already exist, and if they do, whether they are duplicated or have overlapping schedules.
					var existingList = await context.limitSettings.Where(x => x.DNIS == entityDTO.DNIS && x.IsEnabled == true && x.LimitSettingsId != entityDTO.LimitSettingsId).ToListAsync();

					foreach (var existingItem in existingList)
					{
						if (entityDTO.IsEnabled &&
								(!entityDTO.IsScheduled || (entityDTO.IsScheduled && !existingItem.IsScheduled)))
						{
							var message = $"The specified DNIS is already in use in '{existingItem.Description}'";
							_logger.LogError($"CLM:PostDNISSettings {message}.'");
							return message;
						}

						if (entityDTO.IsEnabled  && (existingItem.IsScheduled && entityDTO.IsScheduled))
						{
						
							var isOverlap = (existingItem.ScheduleStart >= entityDTO.ScheduleStart && existingItem.ScheduleStart <= entityDTO.ScheduleEnd) ||
											(existingItem.ScheduleEnd >= entityDTO.ScheduleStart && existingItem.ScheduleEnd <= entityDTO.ScheduleEnd) ||
											(entityDTO.ScheduleStart >= existingItem.ScheduleStart && entityDTO.ScheduleEnd <= existingItem.ScheduleEnd);

							if (isOverlap)
							{
								var message = $"The schedule time range for the specified DNIS overlaps with '{existingItem.Description}'";
								_logger.LogError($"CLM:PostDNISSettings {message}");
								return message;
							}

						}

					}


					DateTime currentDateTime = DateTime.Now;

					string? userName = _httpContextAccessor.HttpContext!.Session.GetString("userName");
					string? period = GetPT(entityDTO.ScheduleStart, entityDTO.ScheduleEnd);

					LimitSettings entity = new LimitSettings
					{
						LimitSettingsId = entityDTO.LimitSettingsId,
						Description = entityDTO.Description,
						DNIS = entityDTO.DNIS,
						IsAllBusy = entityDTO.IsAllBusy,
						Type = entityDTO.Type,
						MaxLimitValue = entityDTO.MaxLimitValue,
						AvailableAgentsLimitValue = entityDTO.AvailableAgentsLimitValue,
						Conditions = entityDTO.Conditions,
						IsScheduled = entityDTO.IsScheduled,
						ScheduleStart = entityDTO.ScheduleStart,
						ScheduleEnd = entityDTO.ScheduleEnd,
						ScheduleStartString = entityDTO.ScheduleStart.ToString("yyyyMMddTHHmmss"),
						ScheduleENDPT = period,

						IsEnabled = entityDTO.IsEnabled,
						IsSettingsError = entityDTO.IsSettingsError,
						DateCreated = currentDateTime,
						CreatedBy = userName,
						DateModified = currentDateTime,
						LastModifiedBy = String.Empty,
						DNISListLMS = entityDTO.DNISList.Select(d => new DNISListLMS { DNIS = d.DNIS, LimitSettingsId = d.LimitSettingsId }).ToList(),
						QueueListLMS = entityDTO.QueueList.Select(q => new QueueListLMS { QueueId = q.QueueId, QueueName = q.QueueName, LimitSettingsId = q.LimitSettingsId }).ToList(),
						DivisionId = entityDTO.DivisionId,
						DivisionName = entityDTO.DivisionName,

					};

					await context.limitSettings.AddAsync(entity);
					await context.SaveChangesAsync();
					_logger.LogInformation($"CLM:PostDNISSettings saved");

					await DeleteDNISSettingsREDIS(entity.DNIS!,entity.LimitSettingsId!);
					_logger.LogInformation($"CLM:PostDNISSettings Delete from REDIS");

					// Update Redis if enabled
					if (entity.IsEnabled)
					{
						await PostDNISSettingsREDIS(entity);
						await CreateConcurrentDNISkeyREDIS(entity);
						_logger.LogInformation($"CLM:PostDNISSettings REDIS success");

					}

					await UpdateQueueListREDIS();
					_logger.LogInformation($"CLM:PostDNISSettings UpdateQueueListREDIS success");

					if (!String.IsNullOrEmpty(entityDTO.DNIS) && !String.IsNullOrEmpty(entityDTO.DivisionId))
					{
                        await CreateDNISdivisionIdREDIS(entityDTO.DNIS, entityDTO.DivisionId);
						_logger.LogInformation($"CLM:PostDNISSettings CreateDNISdivisionIdREDIS success");
					}

					_logger.LogInformation($"CLM:PostDNISSettings success {entity.LimitSettingsId}");

					// Returns the LimitSettingsId if the operation is successful.
					return entity.LimitSettingsId!;
				}


			}
			catch (Exception ex)
			{
				_logger.LogError(ex.Message, "CLM:Error in PostDNISSettings");

				throw;
			}

        }


		public async Task<string> PutDNISSettings(string limitSettingsId, LimitSettingsDTO entityDTO)
        {
			_logger.LogInformation($"CLM:PutDNISSettings limitSettingsId:{limitSettingsId}");
			try
			{
				using (var context = _contextFactory.CreateDbContext())
				{

					var divIDinExistingList = await context.limitSettings.Where(x => x.DNIS == entityDTO.DNIS && x.LimitSettingsId != entityDTO.LimitSettingsId).ToListAsync();
					if (divIDinExistingList.Count > 0 && divIDinExistingList.Select(x => x.DivisionId).FirstOrDefault() != entityDTO.DivisionId)
					{
						string? divName = divIDinExistingList.Select(x => x.DivisionName).FirstOrDefault();
						_logger.LogError($"CLM:PutDNISSettings The specified DNIS is already inuse in the division '{divName}'");
						return $"The specified DNIS is already inuse in the division '{divName}'";
					}

					//Check if the enabled limitSettings already exist, and if they do, whether they are duplicated or have overlapping schedules.
					var existingList = await context.limitSettings.Where(x => x.DNIS == entityDTO.DNIS && x.IsEnabled == true && x.LimitSettingsId != entityDTO.LimitSettingsId).ToListAsync();

					foreach (var existingItem in existingList)
					{
						if (entityDTO.IsEnabled &&
								(!entityDTO.IsScheduled || (entityDTO.IsScheduled && !existingItem.IsScheduled)))
						{
							var message = $"The specified DNIS is already in use in '{existingItem.Description}'";
							_logger.LogError($"CLM:PutDNISSettings {message}.'");
							return message;
						}

						if (entityDTO.IsEnabled && (existingItem.IsScheduled && entityDTO.IsScheduled))
						{

							var isOverlap = (existingItem.ScheduleStart >= entityDTO.ScheduleStart && existingItem.ScheduleStart <= entityDTO.ScheduleEnd) ||
											(existingItem.ScheduleEnd >= entityDTO.ScheduleStart && existingItem.ScheduleEnd <= entityDTO.ScheduleEnd) ||
											(entityDTO.ScheduleStart >= existingItem.ScheduleStart && entityDTO.ScheduleEnd <= existingItem.ScheduleEnd);

							if (isOverlap)
							{
								var message = $"The schedule time range for the specified DNIS overlaps with '{existingItem.Description}'";
								_logger.LogError($"CLM:PutDNISSettings {message}");
								return message;
							}

						}

					}

					string? userName = _httpContextAccessor.HttpContext!.Session.GetString("userName");
					string? period = GetPT(entityDTO.ScheduleStart, entityDTO.ScheduleEnd);

					var _entity = await context.limitSettings.FirstOrDefaultAsync(x => x.LimitSettingsId == limitSettingsId);
					var existingDNISList = await context.DNISListLMS.Where(d => d.LimitSettingsId == limitSettingsId).ToListAsync();
					var existingQueueList = await context.QueueListLMS.Where(q => q.LimitSettingsId == limitSettingsId).ToListAsync();

					context.DNISListLMS.RemoveRange(existingDNISList);
					context.QueueListLMS.RemoveRange(existingQueueList);

					if (existingDNISList.Count > 0)
					{
						await DeleteConcurrentDNISkeyREDIS(existingDNISList, limitSettingsId);
						_logger.LogInformation($"CLM:PutDNISSettings DeleteConcurrentDNISkeyREDIS success");
					}

					await context.SaveChangesAsync();
					_logger.LogInformation($"CLM:PutDNISSettings saved");

					DateTime currentDateTime = DateTime.Now;

					if (_entity != null)
					{
						_entity.DNIS = entityDTO.DNIS;
						_entity.Description = entityDTO.Description;
						_entity.IsAllBusy = entityDTO.IsAllBusy;
						_entity.Type = entityDTO.Type;
						_entity.MaxLimitValue = entityDTO.MaxLimitValue;
						_entity.AvailableAgentsLimitValue = entityDTO.AvailableAgentsLimitValue;
						_entity.Conditions = entityDTO.Conditions;
						_entity.IsScheduled = entityDTO.IsScheduled;
						_entity.ScheduleStart = entityDTO.ScheduleStart;
						_entity.ScheduleEnd = entityDTO.ScheduleEnd;
						_entity.ScheduleStartString = entityDTO.ScheduleStart.ToString("yyyyMMddTHHmmss");
						_entity.ScheduleENDPT = period;
						_entity.IsEnabled = entityDTO.IsEnabled;
						_entity.IsSettingsError = entityDTO.IsSettingsError;
						_entity.LastModifiedBy = userName;
						_entity.DateModified = currentDateTime;
						_entity.DNISListLMS = entityDTO.DNISList.Select(d => new DNISListLMS { DNIS = d.DNIS, LimitSettingsId = d.LimitSettingsId }).ToList();

						if (entityDTO.QueueList != null) {
                            _entity.QueueListLMS = entityDTO.QueueList.Select(q => new QueueListLMS { QueueId = q.QueueId, QueueName = q.QueueName, LimitSettingsId = q.LimitSettingsId }).ToList();
                        }

                        _entity.DivisionId = entityDTO.DivisionId;
						_entity.DivisionName = entityDTO.DivisionName;

						await context.SaveChangesAsync();
						_logger.LogInformation($"CLM:PostDNISSettings _entity was not null.saved");

						await DeleteDNISSettingsREDIS(entityDTO.DNIS!, entityDTO.LimitSettingsId!);
						_logger.LogInformation($"CLM:PutDNISSettings Delete from REDIS");

						// Update Redis if the limitSetting was set to enabled
						if (_entity.IsEnabled)
						{
							await PostDNISSettingsREDIS(_entity);
							await CreateConcurrentDNISkeyREDIS(_entity);
							_logger.LogInformation($"CLM:PutDNISSettings REDIS success");

						}

						await UpdateQueueListREDIS();
						_logger.LogInformation($"CLM:PutDNISSettings UpdateQueueListREDIS success");

						if (!String.IsNullOrEmpty(entityDTO.DNIS) && !String.IsNullOrEmpty(entityDTO.DivisionId))
                        {
                            await CreateDNISdivisionIdREDIS(entityDTO.DNIS, entityDTO.DivisionId);
							_logger.LogInformation($"CLM:PutDNISSettings CreateDNISdivisionIdREDIS success");
						}

						_logger.LogInformation($"CLM:PutDNISSettings success {_entity.LimitSettingsId}");

						// Returns the LimitSettingsId if the operation is successful.
						return _entity.LimitSettingsId!;
					}

					return "";

				}

			}
			catch (Exception ex)
			{
				_logger.LogError(ex.Message, "CLM:Error in PutDNISSettings");

				throw;
			}
			
        }

        public async Task<string> DeleteDNISSettings(string limitSettingsId)
        {
			_logger.LogInformation($"CLM:DeleteDNISSettings limitSettingsId:{limitSettingsId}");
			try
			{
				using (var context = _contextFactory.CreateDbContext())
				{
					var _entity = await context.limitSettings.FirstOrDefaultAsync(x => x.LimitSettingsId == limitSettingsId);
					var existingDNISList = await context.DNISListLMS.Where(d => d.LimitSettingsId == limitSettingsId).ToListAsync();
					var existingQueueList = await context.QueueListLMS.Where(q => q.LimitSettingsId == limitSettingsId).ToListAsync();

					context.DNISListLMS.RemoveRange(existingDNISList);
                    if (existingDNISList.Count > 0)
                    {
                        await DeleteConcurrentDNISkeyREDIS(existingDNISList, limitSettingsId);
                    }

                    context.QueueListLMS.RemoveRange(existingQueueList);

					if (_entity != null)
					{
						context.limitSettings.Remove(_entity);
						await context.SaveChangesAsync();
						_logger.LogInformation($"CLM:DeleteDNISSettings saved");

						await DeleteDNISSettingsREDIS(_entity.DNIS!, _entity.LimitSettingsId!);
						_logger.LogInformation($"CLM:DeleteDNISSettings Delete from REDIS");

						await UpdateQueueListREDIS();
						_logger.LogInformation($"CLM:DeleteDNISSettings UpdateQueueListREDIS success");

						if (!String.IsNullOrEmpty(_entity.DNIS))
                        {
							int DNISCountinLimitSettings = await context.limitSettings.Where(x=>x.DNIS==_entity.DNIS).CountAsync();
							if (DNISCountinLimitSettings == 0)
							{
                                await DeleteDNISdivisionIdREDIS(_entity.DNIS);
								_logger.LogInformation($"CLM:DeleteDNISSettings DeleteDNISdivisionIdREDIS success");
							}
                        }

						_logger.LogInformation($"CLM:DeleteDNISSettings success {_entity.LimitSettingsId}");

						// Returns the LimitSettingsId if the operation is successful.
						return _entity.LimitSettingsId!;

					}

					_logger.LogInformation($"CLM:DeleteDNISSettings failed return null");

					return "";
				}

			}
			catch (Exception ex)
			{
				_logger.LogError(ex.Message, "CLM:Error in DeleteDNISSettings");

				throw;
			}
            
        }


        public async Task<List<ANIList>> GetANISettingsAsync()
        {
            _logger.LogInformation("CLM:GetANISettingsAsync");

            try
            {
                using (var context = _contextFactory.CreateDbContext())
                {
					var ANISettings = await context.ANIList.ToListAsync();

                    _logger.LogInformation("CLM:GetANISettingsAsync success");

                    return ANISettings.Select(ls => new ANIList
                    {
                        ANI = ls.ANI,
                        DateCreated = ls.DateCreated,
                        CreatedBy = ls.CreatedBy

                    }).ToList();

                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message, "CLM:Error in GetANISettingsAsync");

                throw;
            }

        }


        public async Task<string> PostANISettings(string ani)
        {
            _logger.LogInformation($"CLM:PostANISettings");
            try
            {
                using (var context = _contextFactory.CreateDbContext())
                {

                    var existingANI = await context.ANIList.Where(x => x.ANI == ani).ToListAsync();
                    if (existingANI!=null && existingANI.Count > 0)
                    {
                        _logger.LogError($"CLM:PostANISettings The specified ANI is already exists");
                        return $"The specified ANI is already exists";
                    }

                    DateTime currentDateTime = DateTime.Now;

                    string? userName = _httpContextAccessor.HttpContext!.Session.GetString("userName");
                   
                    ANIList entity = new ANIList
                    {
                        ANI = ani,
                        DateCreated = currentDateTime,
                        CreatedBy = userName,

                    };

                    await context.ANIList.AddAsync(entity);
                    await context.SaveChangesAsync();
                    _logger.LogInformation($"CLM:PostANISettings saved");

                    await _redisDBAsync.SetAddAsync("ANIList", entity.ANI);

                    _logger.LogInformation($"CLM:PostANISettings success {entity.ANI}");

                    return entity.ANI!;
                }


            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message, "CLM:Error in PostANISettings");

                throw;
            }

        }


		public async Task<string> DeleteANISettings(string ani)
		{
			_logger.LogInformation($"CLM:DeleteANISettings ANI:{ani}");
			try
			{
				using (var context = _contextFactory.CreateDbContext())
				{
					var _entity = await context.ANIList.FirstOrDefaultAsync(x => x.ANI == ani);


					if (_entity != null)
					{
						context.ANIList.Remove(_entity);
						await context.SaveChangesAsync();
						_logger.LogInformation($"CLM:DeleteANISettings saved");

                        await _redisDBAsync.SetRemoveAsync("ANIList", ani);
                        _logger.LogInformation($"CLM:DeleteANISettings Delete {ani} from REDIS");

						_logger.LogInformation($"CLM:DeleteANISettings success {ani}");

						return _entity.ANI!;

					}

					_logger.LogInformation($"CLM:DeleteANISettings failed return null");

					return "";
				}

			}
			catch
			{
                _logger.LogInformation($"CLM:DeleteANISettings failed return null");
				return "";

            }
        }

        public async Task PostDNISSettingsREDIS(LimitSettings entity)
		{
			_logger.LogInformation($"CLM:PostDNISSettingsREDIS");

			try
			{
				var enabledDNISKey = $"enabledDNIS:{entity.DNIS}";
                await _redisDBAsync.SetAddAsync(enabledDNISKey, entity.LimitSettingsId);

                var scheduledKey = $"scheduled:{entity.DNIS}"+"_"+$"{entity.LimitSettingsId}";
                var scheduledHashFields = new HashEntry[]
                {
                    new HashEntry("start", entity.IsScheduled ? $"{entity.ScheduleStartString}" : "0000/00/00 00:00:00"),
                    new HashEntry("end", $"{entity.ScheduleENDPT}")
                };
                await _redisDBAsync.HashSetAsync(scheduledKey, scheduledHashFields);

                var limitStatusKey = $"limitStatus:{entity.DNIS}"+"_"+$"{entity.LimitSettingsId}";

                List<HashEntry> limitStatusHashFields = new List<HashEntry>();
                var limitSettingsIdHashEntry = new HashEntry("limitSettingsId", $"{entity.LimitSettingsId}");


				if (entity.IsAllBusy)
				{
					limitStatusHashFields.Add(new HashEntry("status", "AllBusy"));
					limitStatusHashFields.Add(limitSettingsIdHashEntry);
				}
				else
				{
					if (entity.Type == "Concurrent Calls")
					{
						// ConcurrentCalls
						limitStatusHashFields.Add(new HashEntry("status", "ConcurrentCalls"));
						limitStatusHashFields.Add(limitSettingsIdHashEntry);

					}

					if (entity.Type == "Concurrent Calls SUM")
					{

						limitStatusHashFields.Add(new HashEntry("status", "ConcurrentCallsSUM"));
						limitStatusHashFields.Add(limitSettingsIdHashEntry);

						// add this DNIS to relatedDNIS
						await _redisDBAsync.SetAddAsync($"relatedDNISList:{entity.DNIS}" + "_" + $"{entity.LimitSettingsId}", entity.DNIS!.ToString());

						foreach (var eachConcDNIS in entity.DNISListLMS!)
						{
							await _redisDBAsync.SetAddAsync($"relatedDNISList:{entity.DNIS}" + "_" + $"{entity.LimitSettingsId}", eachConcDNIS.DNIS!.ToString());

                        }

                    }

					var queueTypes = new[] { "Calls Waiting", "Queue Interactions", "Calls Waiting+Interactions", "Queue Available Agents" };
					if (entity.Type != null && queueTypes.Contains(entity.Type))
					{
						// Append SUM or OR to Type  
						var typeValue = entity.Type.Replace(" ", "");
						var statusValue = entity.Conditions == "SUM" ? typeValue + "SUM" : typeValue + "OR";

						limitStatusHashFields.Add(new HashEntry("status", statusValue));
						limitStatusHashFields.Add(limitSettingsIdHashEntry);

						foreach (var eachQueue in entity.QueueListLMS!)
						{
							await _redisDBAsync.SetAddAsync($"relatedQueueList:{entity.DNIS}" + "_" + $"{entity.LimitSettingsId}", eachQueue.QueueId!.ToString());
						}
					}

				}

				// limitSettings
				await _redisDBAsync.HashSetAsync(limitStatusKey, limitStatusHashFields.ToArray());

				// limitValue
				if (entity.Type == "Queue Available Agents")
				{
					await _redisDBAsync.StringSetAsync($"limitValue:{entity.DNIS}" + "_" + $"{entity.LimitSettingsId}", $"{entity.AvailableAgentsLimitValue}");
				}
				else
				{
					await _redisDBAsync.StringSetAsync($"limitValue:{entity.DNIS}" + "_" + $"{entity.LimitSettingsId}", $"{entity.MaxLimitValue}");
				}

				_logger.LogInformation($"CLM:PostDNISSettingsREDIS success");

			}
			catch (Exception ex)
			{
				_logger.LogError(ex.Message, "CLM:Error in PostDNISSettingsREDIS");

				throw;
			}

		}

		public async Task CreateConcurrentDNISkeyREDIS(LimitSettings entity)
		{
			_logger.LogInformation($"CLM:CreateConcurrentDNISkeyREDIS");

			if (entity.DNISListLMS!.Count == 0) return;
			if (entity.IsAllBusy) return;

            await _redisDBAsync.HashSetAsync($"concurrentDNISList", "divId:" + entity.DNIS!.ToString(), entity.LimitSettingsId!.ToString());
            await _redisDBAsync.HashSetAsync($"concurrentDNISList", "lmtId:" + entity.DNIS!.ToString(), entity.LimitSettingsId!.ToString());

            try
			{
				foreach (var conDNIS in entity.DNISListLMS)
				{
					await _redisDBAsync.HashSetAsync($"concurrentDNISList", "divId:"+conDNIS.DNIS!.ToString(), entity.DivisionId!.ToString());
                    await _redisDBAsync.HashSetAsync($"concurrentDNISList", "lmtId:" + conDNIS.DNIS!.ToString(), entity.LimitSettingsId!.ToString());
                }

				_logger.LogInformation($"CLM:CreateConcurrentDNISkeyREDIS success");

			}
			catch (Exception ex)
			{
				_logger.LogError(ex.Message, "CLM:Error in CreateConcurrentDNISkeyREDIS");

				throw;
			}

		}


		public async Task DeleteConcurrentDNISkeyREDIS(List<DNISListLMS> DNISListLMS,string limitSettingsId)
		{
			_logger.LogInformation($"CLM:DeleteConcurrentDNISkeyREDIS");

			var dnislist = DNISListLMS.Select(x => x.DNIS).ToList();

			try
			{
				foreach (var conDNIS in dnislist)
				{
                    await _redisDBAsync.HashDeleteAsync("concurrentDNISList", "divId:"+conDNIS);
                    await _redisDBAsync.HashDeleteAsync("concurrentDNISList", "lmtId:" + conDNIS);
                }

				_logger.LogInformation($"CLM:DeleteConcurrentDNISkeyREDIS success");

			}
			catch (Exception ex)
			{
				_logger.LogError(ex.Message, "CLM:Error in DeleteConcurrentDNISkeyREDIS");

				throw;
			}

		}

		public async Task UpdateQueueListREDIS()
		{
			_logger.LogInformation($"CLM:UpdateQueueListREDIS");

			List<string?> enabledQueueIds = new List<string?>();

			try
			{
				using (var context = _contextFactory.CreateDbContext())
				{
					enabledQueueIds = await context.limitSettings.Where(x => x.IsEnabled == true).SelectMany(x => x.QueueListLMS!).Select(x => x.QueueId).Distinct().ToListAsync();
				}

				var currentQueueIDListRedis = (await _redisDBAsync.SetMembersAsync("queueIDList")).Select(x => x.ToString()).ToList();

				var idsToRemove = currentQueueIDListRedis.Except(enabledQueueIds).ToList();

				var idsToAdd = enabledQueueIds.Except(currentQueueIDListRedis).ToList();

				foreach (var id in idsToRemove)
				{
					await _redisDBAsync.SetRemoveAsync("queueIDList", id);
				}

				foreach (var id in idsToAdd)
				{
					await _redisDBAsync.SetAddAsync("queueIDList", id);
				}

				_logger.LogInformation($"CLM:UpdateQueueListREDIS success");

			}
			catch (Exception ex)
			{
				_logger.LogError(ex.Message, "CLM:Error in UpdateQueueListREDIS");

				throw;
			}

		}

		public async Task DeleteDNISSettingsREDIS(string DNIS,string limitSettingsId)
		{
			_logger.LogInformation($"CLM:DeleteDNISSettingsREDIS DNIS:{DNIS}");

            try
            {
				await _redisDBAsync.KeyDeleteAsync($"scheduled:{DNIS}" + "_" + $"{limitSettingsId}");
				await _redisDBAsync.KeyDeleteAsync($"limitStatus:{DNIS}" + "_" + $"{limitSettingsId}");
				await _redisDBAsync.KeyDeleteAsync($"limitValue:{DNIS}" + "_" + $"{limitSettingsId}");
				await _redisDBAsync.KeyDeleteAsync($"relatedDNISList:{DNIS}" + "_" + $"{limitSettingsId}");
				await _redisDBAsync.KeyDeleteAsync($"relatedQueueList:{DNIS}" + "_" + $"{limitSettingsId}");
                await _redisDBAsync.SetRemoveAsync($"enabledDNIS:{DNIS}", $"{limitSettingsId}");

				_logger.LogInformation($"CLM:DeleteDNISSettingsREDIS success");

			}
			catch (Exception ex)
			{
				_logger.LogError(ex.Message, "CLM:Error in DeleteDNISSettingsREDIS");

				throw;
			}

		}

		public async Task CreateDNISdivisionIdREDIS(string? DNIS,string? divisionID)
		{
            _logger.LogInformation($"CLM:CreateDNISdivisionIdREDIS DNIS:{DNIS} DivId:{divisionID}");

			try
			{
				await _redisDBAsync.StringSetAsync($"dnisDivision:{DNIS}", divisionID);

				_logger.LogInformation($"CLM:CreateDNISdivisionIdREDIS success");
			}
			catch (Exception ex)
			{
                _logger.LogError(ex.Message, "CLM:Error in CreateDNISdivisionIdREDIS");

                throw;
			}
        }

        public async Task DeleteDNISdivisionIdREDIS(string? DNIS)
        {
            _logger.LogInformation($"CLM:DeleteDNISdivisionIdREDIS DNIS:{DNIS}");
            try
            {
                await _redisDBAsync.KeyDeleteAsync($"dnisDivision:{DNIS}");

				_logger.LogInformation($"CLM:DeleteDNISdivisionIdREDIS success");

			}
            catch (Exception ex)
            {
                _logger.LogError(ex.Message, "CLM:Error in DeleteDNISdivisionIdREDIS");

                throw;
            }
        }

		public async Task<string> RecoveryREDIS(string? setting)
		{
			_logger.LogInformation($"CLM:RecoveryREDIS");

			try
			{
				bool result = await _redisDBAsync.StringSetAsync("systemSettings", setting);
				
				_logger.LogInformation($"CLM:RecoveryREDIS systemSettings:{setting} result:{result}");

				if (!result)
				{
					_logger.LogError("CLM:Error Unable to connect to REDIS server.");
					return "Unable to connect to REDIS server.";
				}

			}
			catch (Exception ex)
			{
				_logger.LogError("CLM:Error systemSettings in RecoveryREDIS" + ex);
				throw;
			}

			try
			{
				using (var context = _contextFactory.CreateDbContext())
				{
					var existingLimitSettingsList = await context.limitSettings.ToListAsync();


					foreach (var existingItem in existingLimitSettingsList)
					{
						await DeleteDNISSettingsREDIS(existingItem.DNIS!, existingItem.LimitSettingsId!);
						_logger.LogInformation($"CLM:RecoveryREDIS Delete {existingItem.LimitSettingsId} from REDIS");

						// Update Redis if enabled
						if (existingItem.IsEnabled)
						{

							var existingDNISList = await context.DNISListLMS.Where(d => d.LimitSettingsId == existingItem.LimitSettingsId).ToListAsync();

							if (existingDNISList != null)
							{
								existingItem.DNISListLMS = existingDNISList.Select(d => new DNISListLMS { DNIS = d.DNIS, LimitSettingsId = d.LimitSettingsId }).ToList();
							}

							var existingQueueList = await context.QueueListLMS.Where(q => q.LimitSettingsId == existingItem.LimitSettingsId).ToListAsync();

							if (existingQueueList != null)
							{
								existingItem.QueueListLMS = existingQueueList.Select(q => new QueueListLMS { QueueId = q.QueueId, QueueName = q.QueueName, LimitSettingsId = q.LimitSettingsId }).ToList();
							}

							await PostDNISSettingsREDIS(existingItem);

							if (existingItem.DNISListLMS != null)
							{
								await CreateConcurrentDNISkeyREDIS(existingItem);

							}

							_logger.LogInformation($"CLM:RecoveryREDIS Create {existingItem.LimitSettingsId} REDIS success");

						}

						await CreateDNISdivisionIdREDIS(existingItem.DNIS, existingItem.DivisionId);
						_logger.LogInformation($"CLM:RecoveryREDIS CreateDNISdivisionIdREDIS {existingItem.LimitSettingsId} success");

					}

					await UpdateQueueListREDIS();

					var existingANIList = await context.ANIList.ToListAsync();

					foreach( var existingItem in existingANIList)
					{
						await _redisDBAsync.SetAddAsync("ANIList", existingItem?.ANI);
					}





					_logger.LogInformation($"CLM:RecoveryREDIS success");

					// Returns the LimitSettingsId if the operation is successful.
					return "Success";
				}

	






			}
			catch (Exception ex)
			{
				_logger.LogError(ex.Message, "CLM:Error in RecoveryREDIS");

				throw;
			}
		}


        public async Task<List<UserDivision>> FetchUserDivision(string accessToken)
		{
			_logger.LogInformation($"CLM:FetchUserDivision");

			try
			{
				if (String.IsNullOrEmpty(accessToken))
				{
					accessToken = _httpContextAccessor.HttpContext!.Session.GetString("GCToken")!;
				}

				int page = 1;
				int pageCount = 0;

				List<UserDivision> userDivisions = new List<UserDivision>();
				_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
				do
				{
					var response = await _httpClient.GetAsync($"https://api.{_environment}/api/v2/authorization/divisionspermitted/paged/me?permission=routing%3Aqueue%3Aview&pageNumber={page}&pageSize=50");

					var divInfo = JObject.Parse(await response.Content.ReadAsStringAsync())["entities"];

					if (divInfo != null)
					{
						foreach (var entity in divInfo)
						{
							UserDivision userDivision = new UserDivision
							{
								DivisionId = entity["id"]!.ToString(),
								DivisionName = entity["name"]!.ToString()
							};

							if ((bool)entity["homeDivision"]!)
							{
								userDivisions.Insert(0, userDivision);
							}
							else
							{
								userDivisions.Add(userDivision);
							}
						}
					}

					page++;
				} while (page <= pageCount);

				_logger.LogInformation($"CLM:FetchUserDivision success");

				return userDivisions;


			}
			catch (Exception ex)
			{
				_logger.LogError(ex.Message, "CLM:Error in FetchUserDivision");

				throw;
			}
			
		}


		public async Task<string> FetchTokenFromCode(string code)
        {
			_logger.LogInformation($"CLM:FetchTokenFromCode");

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "authorization_code"),
                new KeyValuePair<string, string>("code", code),
                new KeyValuePair<string, string>("redirect_uri", _redirectUri!)
            });

			try
			{
                var basicAuth = Convert.ToBase64String(Encoding.GetEncoding("ISO-8859-1").GetBytes(_clientId + ":" + _clientSecret));
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
                var response = await _httpClient.PostAsync($"https://login.{_environment}/oauth/token", content);
                var token = JObject.Parse(await response.Content.ReadAsStringAsync())["access_token"]!.ToString();

				_logger.LogInformation($"CLM:FetchTokenFromCode success");

				return token;

			}
			catch (Exception ex)
			{
                _logger.LogError(ex.Message, "CLM:Error in FetchTokenFromCode");

                throw;
			}

        }

        public async Task<JObject> FetchUserInfo(string accessToken)
        {
			_logger.LogInformation($"CLM:FetchUserInfo");
			try
			{
				_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
				var response = await _httpClient.GetAsync($"https://api.{_environment}/api/v2/users/me");

				_logger.LogInformation($"CLM:FetchUserInfo success");

				return JObject.Parse(await response.Content.ReadAsStringAsync());
			}
			catch (Exception ex)
			{
				_logger.LogError(ex.Message, "CLM:Error in FetchUserInfo");

				throw;
			}

        }

        public async Task<JObject> FetchUserOrgInfo(string accessToken)
        {
            _logger.LogInformation($"CLM:FetchUserInfo");
            try
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                var response = await _httpClient.GetAsync($"https://api.{_environment}/api/v2/organizations/me");

				var orgContents = JObject.Parse(await response.Content.ReadAsStringAsync());

				string? orgName = (string)orgContents["domain"]!;

				if (orgName != _orgname)
				{
					_logger.LogInformation($"CLM:Tried to logon to wrong org.");
                    return JObject.FromObject(new { status = "orgError", message = "Invalid organization", name="" });
                }

				_logger.LogInformation($"CLM:FetchUserInfo success");

				return orgContents;

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message, "CLM:Error in FetchUserOrgInfo");

                throw;
            }

        }


        public async Task<List<QueueListDTO>> FetchQueueList()
        {
			_logger.LogInformation($"CLM:FetchQueueList");
			try
			{
				string? accessToken = _httpContextAccessor.HttpContext!.Session.GetString("GCToken")!;
				_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

				List<QueueListDTO> queueListDTO = new List<QueueListDTO>();

				int page = 1;
				int pageCount = 0;

				do
				{
					var response = await _httpClient.GetAsync($"https://api.{_environment}/api/v2/routing/queues?pageNumber={page}&pageSize=100");

					var responseContent = await response.Content.ReadAsStringAsync();
					var jsonResponse = JObject.Parse(responseContent);

					if (jsonResponse["pageCount"] != null)
					{
						pageCount = (int)jsonResponse["pageCount"]!;
					}
					else
					{
						return null!;
					}

					var entities = JObject.Parse(await response.Content.ReadAsStringAsync())["entities"];
					if (entities != null)
					{
						foreach (var item in entities)
						{
							QueueListDTO gcQueue = new QueueListDTO
							{
								QueueId = item["id"]!.ToString(),
								QueueName = item["name"]!.ToString(),
								DivisionId = item?["division"]?["id"]!.ToString(),
								DivisionName = item?["division"]?["name"]!.ToString()
							};

							queueListDTO.Add(gcQueue);

						}
					}

					page++;

				} while (page <= pageCount);

				_logger.LogInformation($"CLM:FetchQueueList success");

				return queueListDTO;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex.Message, "CLM:Error in FetchQueueList");

				throw;
			}
			
        }

        public string GetPT(DateTime start,DateTime end)
        {
			_logger.LogInformation($"CLM:GetPT");
			LocalDateTime Localstart = LocalDateTime.FromDateTime(start);
			LocalDateTime Localend = LocalDateTime.FromDateTime(end);
			NodaTime.Period period = NodaTime.Period.Between(Localend, Localstart);

			_logger.LogInformation($"CLM:GetPT success");

			return period.ToString().Replace("-", "");
		}

        public async Task<List<AccDTO>> GetAccAsync(DateTime? start, DateTime? end, string? userDivisionsId = null)
        {
            _logger.LogInformation($"CLM:GetAccAsync");
            if (userDivisionsId == null)
            {
                userDivisionsId = _httpContextAccessor.HttpContext!.Session.GetString("userDivisionsId")!;
            }

            string[] divisionIds = userDivisionsId.Split('|');

            try
            {
                using (var context = _contextFactory.CreateDbContext())
                {
                    var accs = await context.acc.Where(x => x.Time >= start && x.Time <= end).ToListAsync();
                    var limitSettings = await context.limitSettings.ToListAsync();

                    var filteredAccs = accs
                        .Where(x => string.IsNullOrEmpty(x.LimitSettingsId)
                            || x.LimitSettingsId.Contains("Unlimited Mode")
                            || x.LimitSettingsId.Contains("All Busy Mode")
                            || (x.DivisionId != null && x.DivisionId.Equals(""))
                            || (x.DivisionId != null && divisionIds.Any(d => x.DivisionId.Contains(d))))
                        .ToList();

                    var result = new List<AccDTO>();

                    foreach (var acc in filteredAccs)
                    {
                        var matchedSetting = limitSettings.FirstOrDefault(ls => ls.LimitSettingsId == acc.LimitSettingsId);
                        if (matchedSetting != null)
                        {
                            result.Add(new AccDTO { Acc = acc, Description = matchedSetting.Description });
                        }
                        else
                        {
                            result.Add(new AccDTO { Acc = acc, Description = null });
                        }
                    }

					_logger.LogInformation($"CLM:GetAccAsync success");

					return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message, "CLM:Error in GetAccAsync");

                throw;
            }
        }

		public async Task<ExportResult> ExportAccToExcel(DateTime? startDate, DateTime? endDate, Query? query = null)
		{
			_logger.LogInformation($"CLM:ExportAccToExcel");

			string? userDivisionsId = _httpContextAccessor.HttpContext!.Session.GetString("userDivisionsId")!;

			_logger.LogInformation($"CLM userDivisionsId :{userDivisionsId}");

			int recordCount = await GetRecordCount(startDate, endDate, userDivisionsId);

			if (recordCount > _maxAccExportRecords)
			{
				_logger.LogError($"CLM:ExportAccToExcel Record count exceeds {_maxAccExportRecords}");

				return new ExportResult
				{
					ErrorMessage = "Record count exceeds "+ _maxAccExportRecords.ToString() + ". Please narrow down your search criteria."
				};
			}

			var baseurl = query != null ? query.ToUrl("api/ExportDB/exportacc") : "api/ExportDB/exportacc";

			var parameters = new List<string>();

			if (startDate.HasValue)
			{
				parameters.Add($"startDate={Uri.EscapeDataString(startDate.Value.ToString("yyyy/MM/dd HH:mm:ss"))}");
			}

			if (endDate.HasValue)
			{
				parameters.Add($"endDate={Uri.EscapeDataString(endDate.Value.ToString("yyyy/MM/dd HH:mm:ss"))}");
			}

			if (userDivisionsId != null)
			{
				parameters.Add($"userDivisionsId={userDivisionsId}");
			}

			var url = $"{_navigationManager.BaseUri}{baseurl}&{string.Join("&", parameters)}";

			_logger.LogInformation($"CLM:{url}");

			try
			{
				var response = await _httpClient.GetAsync(url);
				var fileBytes = await response.Content.ReadAsByteArrayAsync();

				_logger.LogInformation($"CLM:ExportAccToExcel success");

				return new ExportResult
				{
					FileBytes = fileBytes
				};

			}
			catch (Exception ex)
			{
				_logger.LogError(ex.Message, "CLM:Error in ExportAccToExcel");

				return new ExportResult
				{
					ErrorMessage = ex.Message
                };

			}

		}

		public async Task<int> GetRecordCount(DateTime? startDate, DateTime? endDate, string? userDivisionsId)
		{
			_logger.LogInformation($"CLM:GetRecordCount");

			using (var context = _contextFactory.CreateDbContext())
			{
                string[] divisionIds = userDivisionsId?.Split('|') ?? new string[0];

                var accs = await context.acc.Where(x => x.Time >= startDate && x.Time <= endDate).ToListAsync();

				_logger.LogInformation($"CLM:GetRecordCount success");
				return accs.Where(x => string.IsNullOrEmpty(x.LimitSettingsId)
						|| x.LimitSettingsId.Contains("All Busy")
						|| x.LimitSettingsId.Contains("CLM")
						|| (x.DivisionId != null && divisionIds.Any(d => x.DivisionId.Contains(d)))).Count();

			}

		}

	}

	public class ExportResult
	{
		public byte[]? FileBytes { get; set; }
		public string? ErrorMessage { get; set; }
	}

}
