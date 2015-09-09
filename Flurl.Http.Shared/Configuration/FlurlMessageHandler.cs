﻿using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Flurl.Http.Content;

namespace Flurl.Http.Configuration
{
	/// <summary>
	/// HTTP message handler used by default in all Flurl-created HttpClients.
	/// </summary>
	public class FlurlMessageHandler : DelegatingHandler
	{
		public FlurlMessageHandler(HttpMessageHandler innerHandler) : base(innerHandler) { }

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
			var settings = request.GetFlurlSettings();

			var call = new HttpCall {
				Request = request
			};

			var stringContent = request.Content as CapturedStringContent;
			if (stringContent != null)
				call.RequestBody = stringContent.Content;

			await RaiseGlobalEventAsync(settings.BeforeCall, settings.BeforeCallAsync, call);

			call.StartedUtc = DateTime.UtcNow;

			try {
				call.Response = await base.SendAsync(request, cancellationToken);
				call.EndedUtc = DateTime.UtcNow;
			}
			catch (Exception ex) {
				call.Exception =  (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested) ?
					new FlurlHttpTimeoutException(call, ex) :
					new FlurlHttpException(call, ex);
			}

			if (call.Response != null && !call.Succeeded) {
				if (call.Response.Content != null)
					call.ErrorResponseBody = await call.Response.Content.ReadAsStringAsync();

				call.Exception = new FlurlHttpException(call, null);
			}

			if (call.Exception != null)
				await RaiseGlobalEventAsync(settings.OnError, settings.OnErrorAsync, call);

			await RaiseGlobalEventAsync(settings.AfterCall, settings.AfterCallAsync, call);

			if (call.Exception != null && !call.ExceptionHandled)
				throw call.Exception;

			call.Response.RequestMessage = request;
			return call.Response;
		}

		private async Task RaiseGlobalEventAsync(Action<HttpCall> syncVersion, Func<HttpCall, Task> asyncVersion, HttpCall call) {
			if (syncVersion != null) syncVersion(call);
			if (asyncVersion != null) await asyncVersion(call);
		}
	}
}
