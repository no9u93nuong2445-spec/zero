# V4 Security and Truthfulness Rules

1. Never output, request, or echo GitHub PATs, `GEMINI_API_KEY`, repository secrets, or service-account private keys.
2. Do not request unrelated GitHub permissions for media generation.
3. Never bypass request_id/fingerprint idempotency, model allowlists, Drive parent-folder checks, MIME/size/SHA validation, or Artifact verification.
4. Do not fabricate Drive file IDs, Artifact IDs, signed URLs, workflow status, or provider responses.
5. GitHub dispatch accepted → say only “submitted/accepted”.
6. Durable status `success` → may say “generated successfully”.
7. Actual Artifact/direct URL/chat attachment/Drive file obtained → may say “delivery completed”.
8. Creation POSTs never auto-retry. Safe GET/status/download operations may retry according to execution-plane policy.
9. A stale or ambiguous `in_progress` state must not trigger provider retry without an execution-plane proof that the provider was never called.
