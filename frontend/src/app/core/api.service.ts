import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AiProposal, DefinitionPreview, EntityDto, FieldDto, OperatorDto, PagedResults, RunDto,
  SaveValidationRequest, ValidationDefinition, ValidationDto, ValidationSummary
} from './models';

/** Thin typed wrapper over the REST API. '/api' is proxied to the .NET backend in development. */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api';

  // Metadata
  entities(): Observable<EntityDto[]> {
    return this.http.get<EntityDto[]>(`${this.base}/metadata/entities`);
  }
  fields(entityKey: string): Observable<FieldDto[]> {
    return this.http.get<FieldDto[]>(`${this.base}/metadata/entities/${encodeURIComponent(entityKey)}/fields`);
  }
  operators(): Observable<OperatorDto[]> {
    return this.http.get<OperatorDto[]>(`${this.base}/metadata/operators`);
  }

  // Validations
  listValidations(search?: string, entity?: string): Observable<ValidationSummary[]> {
    let params = new HttpParams();
    if (search) params = params.set('search', search);
    if (entity) params = params.set('entity', entity);
    return this.http.get<ValidationSummary[]>(`${this.base}/validations`, { params });
  }
  getValidation(id: number): Observable<ValidationDto> {
    return this.http.get<ValidationDto>(`${this.base}/validations/${id}`);
  }
  createValidation(request: SaveValidationRequest): Observable<ValidationDto> {
    return this.http.post<ValidationDto>(`${this.base}/validations`, request);
  }
  updateValidation(id: number, request: SaveValidationRequest): Observable<ValidationDto> {
    return this.http.put<ValidationDto>(`${this.base}/validations/${id}`, request);
  }
  deleteValidation(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/validations/${id}`);
  }
  preview(definition: ValidationDefinition): Observable<DefinitionPreview> {
    return this.http.post<DefinitionPreview>(`${this.base}/validations/preview`, { definition });
  }
  execute(id: number): Observable<RunDto> {
    return this.http.post<RunDto>(`${this.base}/validations/${id}/execute`, null);
  }
  runs(validationId: number): Observable<RunDto[]> {
    return this.http.get<RunDto[]>(`${this.base}/validations/${validationId}/runs`);
  }

  // Runs & results
  getRun(runId: number): Observable<RunDto> {
    return this.http.get<RunDto>(`${this.base}/runs/${runId}`);
  }
  results(runId: number, options: { search?: string; conditionId?: string | null; page: number; pageSize: number }): Observable<PagedResults> {
    let params = new HttpParams().set('page', options.page).set('pageSize', options.pageSize);
    if (options.search) params = params.set('search', options.search);
    if (options.conditionId) params = params.set('conditionId', options.conditionId);
    return this.http.get<PagedResults>(`${this.base}/runs/${runId}/results`, { params });
  }

  // AI
  propose(prompt: string, rootEntity?: string | null): Observable<AiProposal> {
    return this.http.post<AiProposal>(`${this.base}/ai/proposals`, { prompt, rootEntity: rootEntity || null });
  }
}
