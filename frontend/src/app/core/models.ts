// ---------- Validation definition (same JSON model as the backend engine) ----------

export type LogicalOperator = 'And' | 'Or';
export type DataType = 'String' | 'Number' | 'Date' | 'Boolean';
export type OperatorShape = 'None' | 'Single' | 'Range' | 'List';
export type OperandKind = 'Constant' | 'List' | 'Field';
export type Scalar = string | number | boolean | null;

export interface Operand {
  kind: OperandKind;
  value?: Scalar;
  values?: Scalar[];
  field?: string;
}

export interface ConditionGroup {
  type: 'group';
  id: string;
  logic: LogicalOperator;
  children: ConditionNode[];
}

export interface Condition {
  type: 'condition';
  id: string;
  field: string;
  operator: string;
  value?: Operand;
  valueTo?: Operand;
}

export type ConditionNode = ConditionGroup | Condition;

export interface ValidationDefinition {
  rootEntity: string;
  condition: ConditionGroup;
  displayFields?: string[];
}

// ---------- Metadata ----------

export interface EntityDto {
  key: string;
  displayName: string;
  displayNamePlural: string;
  description?: string;
}

export interface FieldDto {
  path: string;
  label: string;
  fieldLabel: string;
  groupLabel: string | null;
  groupPath: string;
  dataType: DataType;
  isMandatory: boolean;
  trueLabel?: string | null;
  falseLabel?: string | null;
  description?: string | null;
}

export interface OperatorDto {
  operator: string;
  label: string;
  dateLabel?: string | null;
  shape: OperatorShape;
  supportedTypes: DataType[];
  allowsFieldOperand: boolean;
}

// ---------- Validations & runs ----------

export type RunStatus = 'Running' | 'Succeeded' | 'Failed';

export interface ValidationSummary {
  id: number;
  name: string;
  description?: string;
  rootEntityKey: string;
  source: 'Manual' | 'AI';
  isActive: boolean;
  updatedAt: string;
  lastRunId?: number | null;
  lastRunStatus?: RunStatus | null;
  lastRunStartedAt?: string | null;
  lastRunDurationMs?: number | null;
  lastRunViolationCount?: number | null;
}

export interface ValidationDto {
  id: number;
  name: string;
  description?: string;
  rootEntity: string;
  source: 'Manual' | 'AI';
  isActive: boolean;
  definition: ValidationDefinition;
  createdAt: string;
  updatedAt: string;
}

export interface SaveValidationRequest {
  name: string;
  description?: string | null;
  definition: ValidationDefinition;
  source?: 'Manual' | 'AI';
}

export interface DefinitionError {
  nodeId?: string | null;
  message: string;
}

export interface ConditionInfo {
  id: string;
  description: string;
}

export interface DefinitionPreview {
  isValid: boolean;
  errors: DefinitionError[];
  description?: string | null;
  conditions: ConditionInfo[];
  generatedSql?: string | null;
}

export interface ResultColumn {
  fieldPath: string;
  label: string;
  dataType: DataType;
}

export interface RunDto {
  runId: number;
  validationId: number;
  status: RunStatus;
  startedAt: string;
  finishedAt?: string | null;
  durationMs?: number | null;
  violationCount?: number | null;
  storedResultCount?: number | null;
  isTruncated: boolean;
  errorMessage?: string | null;
  generatedSql?: string | null;
  columns: ResultColumn[];
  conditions: ConditionInfo[];
}

export interface Reason {
  conditionId: string;
  fieldPath: string;
  message: string;
}

export interface ResultItem {
  rowNumber: number;
  recordKey: string;
  recordTitle: string;
  values: Record<string, string>;
  reasons: Reason[];
}

export interface PagedResults {
  totalCount: number;
  page: number;
  pageSize: number;
  items: ResultItem[];
}

// ---------- AI ----------

export interface AiProposal {
  isValid: boolean;
  errors: string[];
  name?: string | null;
  description?: string | null;
  rootEntity?: string | null;
  definition?: ValidationDefinition | null;
  ruleText?: string | null;
  conditions: ConditionInfo[];
  confidence?: number | null;
  notes: string[];
  rawResponse: string;
  provider: string;
}

/** Draft handed from the AI assistant to the builder via router state. */
export interface BuilderDraft {
  name: string;
  description?: string | null;
  definition: ValidationDefinition;
  source: 'Manual' | 'AI';
}
