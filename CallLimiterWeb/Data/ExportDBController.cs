using Microsoft.AspNetCore.Mvc;
using Radzen;

namespace CallLimiterWeb.Data
{
    [Route("api/[controller]")]
    [ApiController]
    public partial class ExportDBController : ExportController
    {
		private IDNISService _dnisservice;
		private readonly ILogger<DNISService> _logger;

		public ExportDBController(IDNISService dnisservice, ILogger<DNISService> logger)
        {
			_dnisservice = dnisservice;
			_logger = logger;
		}

		[HttpGet("exportacc")]
		public async Task<FileStreamResult> ExportAccToExcel([FromQuery] Query? query, [FromQuery] DateTime? startdate, [FromQuery] DateTime? enddate,string userDivisionsId)
        {
			_logger.LogInformation($"CLM:exportacc");
			List<AccDTO> acclist = await _dnisservice.GetAccAsync(startdate,enddate, userDivisionsId);
			IQueryable<AccDTO> list = acclist.AsQueryable();
			return ToExcel(ApplyQuery(list, Request.Query));

		}

	}
}
