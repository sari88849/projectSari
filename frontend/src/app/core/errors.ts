import { HttpErrorResponse } from '@angular/common/http';
import { DefinitionError } from './models';

/** Extracts a Hebrew, user-facing message from an API error (RFC 7807 problem details). */
export function errorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) return 'לא ניתן להתחבר לשרת. ודאו שה-API פועל.';
    const title = error.error?.title as string | undefined;
    if (title) return title;
    // The API always answers errors with a problem-details JSON body. A 5xx without one comes from the
    // dev-server proxy, which means the API itself is not reachable (not running on port 5080).
    if (error.status >= 500 && (error.error === null || typeof error.error !== 'object')) {
      return 'שרת ה-API אינו זמין. ודאו שה-API פועל (http://localhost:5080) ונסו שוב.';
    }
    if (error.status === 404) return 'הפריט המבוקש לא נמצא';
    if (error.status >= 500) return 'אירעה שגיאה בשרת. נסו שוב מאוחר יותר.';
    return 'הבקשה נכשלה';
  }
  return 'אירעה שגיאה בלתי צפויה';
}

/** Validation errors attached to a 400 problem response, if any. */
export function definitionErrors(error: unknown): DefinitionError[] {
  if (error instanceof HttpErrorResponse && Array.isArray(error.error?.errors)) {
    return error.error.errors as DefinitionError[];
  }
  return [];
}
