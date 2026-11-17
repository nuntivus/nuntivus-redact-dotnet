# Optional ASP.NET Core integration

The NuntivusRedact.AspNetCore package is optional and has a Microsoft.AspNetCore.App framework reference. Standalone applications do not need ASP.NET. Register AddNuntivusRedact with a policy and explicitly chosen detectors/processors, call AddNuntivusRedactHttp, and map MapNuntivusRedact. The complete fourth sample is samples/NuntivusRedact.Sample.WebApi.

Run `dotnet run --project samples/NuntivusRedact.Sample.WebApi --urls http://127.0.0.1:5187`. POST a raw UTF-8 body to `/redact/text`, `/redact/csv`, `/redact/json` or `/redact/xml`. Multipart and Content-Encoding uploads are rejected. The static OpenAPI 3.0.3 description is served at `/openapi/v1.json`. Do not send real PII to development servers.

The handler bounds both Content-Length and actual bytes read (including unknown-length/chunked uploads), sets the server body limit where supported, caps output, honors RequestAborted and stages full output before committing response headers/body. Defaults are 4 MB upload and 8 MB response; each processor's own limits also apply. I/O is asynchronous, but transformations are buffered, not constant-memory streaming. Applications must cap concurrent requests. Responses are attachments with no-store/nosniff headers. Unsupported features return 415, resource limits 413, malformed documents 422 and processing failures 500 with sanitized problem details. Download failure after headers start aborts the connection; discard partial downloads.

The sample clears logging providers to avoid accidental request/document logging. The library logs no document content, filenames, URLs, policy data or exception objects. Production host logging/telemetry is the application's responsibility.

This is NOT a production service. Add authentication, authorization, HTTPS, rate/concurrency limits, deployment isolation and an explicit threat model before exposure outside a trusted local environment. No SaaS billing, durable storage, upload retention, native parsing or Docker worker is provided. If configuring URLs, bind loopback for local testing. Successful HTTP 200 means configured matches were transformed, not complete discovery or anonymization.

Automated handler tests cover accepted bodies, oversized declared/actual lengths, unknown formats, multipart rejection, cancellation and safe headers/errors. Real server smoke checks and all executed evidence belong in PROGRESS.md.
