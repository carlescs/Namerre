/**
 * Browser side of passkeys (WebAuthn): turns the server's JSON options into the binary form
 * `navigator.credentials` takes, and its answer back into WebAuthn JSON for the server. Written out
 * rather than using `PublicKeyCredential.parseCreationOptionsFromJSON`, which older browsers lack.
 */

/** Whether this browser can use passkeys here: WebAuthn needs a secure context (HTTPS or localhost). */
export function passkeysSupported(): boolean {
  return (
    typeof window !== 'undefined' &&
    window.isSecureContext &&
    typeof window.PublicKeyCredential === 'function' &&
    typeof navigator.credentials?.get === 'function'
  );
}

/** The person closed or cancelled the browser's passkey prompt (or it timed out). */
export function passkeyCancelled(error: unknown): boolean {
  return (
    error instanceof DOMException &&
    (error.name === 'NotAllowedError' || error.name === 'AbortError')
  );
}

export function base64UrlToBuffer(value: string): ArrayBuffer {
  const base64 = value.replace(/-/g, '+').replace(/_/g, '/');
  const binary = atob(base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '='));
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes.buffer;
}

export function bufferToBase64Url(buffer: ArrayBuffer | ArrayBufferView): string {
  const bytes =
    buffer instanceof ArrayBuffer
      ? new Uint8Array(buffer)
      : new Uint8Array(buffer.buffer, buffer.byteOffset, buffer.byteLength);
  let binary = '';
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

interface DescriptorJson {
  id: string;
  type: PublicKeyCredentialType;
  transports?: AuthenticatorTransport[];
}

/** `PublicKeyCredentialCreationOptions` as JSON, with binary fields in base64url. */
export interface CreationOptionsJson
  extends Omit<PublicKeyCredentialCreationOptions, 'challenge' | 'user' | 'excludeCredentials'> {
  challenge: string;
  user: { id: string; name: string; displayName: string };
  excludeCredentials?: DescriptorJson[];
}

/** `PublicKeyCredentialRequestOptions` as JSON, with binary fields in base64url. */
export interface RequestOptionsJson
  extends Omit<PublicKeyCredentialRequestOptions, 'challenge' | 'allowCredentials'> {
  challenge: string;
  allowCredentials?: DescriptorJson[];
}

const descriptor = (credential: DescriptorJson): PublicKeyCredentialDescriptor => ({
  type: credential.type,
  id: base64UrlToBuffer(credential.id),
  ...(credential.transports ? { transports: credential.transports } : {}),
});

/** Removes the `null` fields the server writes for unset options, which browsers may reject. */
function withoutNulls<T extends object>(value: T): T {
  return Object.fromEntries(Object.entries(value).filter(([, v]) => v !== null)) as T;
}

export function creationOptions(json: CreationOptionsJson): PublicKeyCredentialCreationOptions {
  return {
    ...withoutNulls(json),
    challenge: base64UrlToBuffer(json.challenge),
    user: { ...json.user, id: base64UrlToBuffer(json.user.id) },
    excludeCredentials: (json.excludeCredentials ?? []).map(descriptor),
  };
}

export function requestOptions(json: RequestOptionsJson): PublicKeyCredentialRequestOptions {
  return {
    ...withoutNulls(json),
    challenge: base64UrlToBuffer(json.challenge),
    allowCredentials: (json.allowCredentials ?? []).map(descriptor),
  };
}

/** A new passkey as WebAuthn JSON (`RegistrationResponseJSON`). */
export function registrationJson(credential: PublicKeyCredential): object {
  const response = credential.response as AuthenticatorAttestationResponse;
  return {
    id: credential.id,
    rawId: bufferToBase64Url(credential.rawId),
    type: credential.type,
    response: {
      clientDataJSON: bufferToBase64Url(response.clientDataJSON),
      attestationObject: bufferToBase64Url(response.attestationObject),
      transports: response.getTransports?.() ?? [],
    },
    clientExtensionResults: credential.getClientExtensionResults(),
  };
}

/** A passkey sign-in as WebAuthn JSON (`AuthenticationResponseJSON`). */
export function authenticationJson(credential: PublicKeyCredential): object {
  const response = credential.response as AuthenticatorAssertionResponse;
  return {
    id: credential.id,
    rawId: bufferToBase64Url(credential.rawId),
    type: credential.type,
    response: {
      clientDataJSON: bufferToBase64Url(response.clientDataJSON),
      authenticatorData: bufferToBase64Url(response.authenticatorData),
      signature: bufferToBase64Url(response.signature),
      ...(response.userHandle ? { userHandle: bufferToBase64Url(response.userHandle) } : {}),
    },
    clientExtensionResults: credential.getClientExtensionResults(),
  };
}
