import { Component, computed, inject, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { resetOperands } from '../../core/definition.utils';
import { MetadataStore } from '../../core/metadata.store';
import { Condition, FieldDto, OperatorDto, Scalar } from '../../core/models';

interface FieldGroup { label: string; fields: FieldDto[]; }

/**
 * One condition row: field -> operator -> value(s). The value editor adapts to the field's business type
 * (number, date, yes/no, text, list) and can compare to another field of the same type.
 * Edits mutate the condition object in place and emit `changed`.
 */
@Component({
  selector: 'app-condition-editor',
  imports: [FormsModule, MatFormFieldModule, MatSelectModule, MatInputModule, MatButtonModule, MatIconModule, MatButtonToggleModule, MatTooltipModule],
  templateUrl: './condition-editor.html',
  styleUrl: './condition-editor.scss'
})
export class ConditionEditor {
  private readonly metadata = inject(MetadataStore);

  readonly condition = input.required<Condition>();
  readonly fields = input.required<FieldDto[]>();
  readonly rootLabel = input('');
  readonly errors = input<string[]>([]);
  readonly changed = output<void>();
  readonly remove = output<void>();

  /** Fields grouped by relationship path for the picker ("עובד", "מחלקה", "מחלקה › מנהל המחלקה", ...). */
  protected readonly fieldGroups = computed<FieldGroup[]>(() => {
    const groups = new Map<string, FieldGroup>();
    for (const f of this.fields()) {
      const label = f.groupLabel ?? this.rootLabel();
      if (!groups.has(f.groupPath)) groups.set(f.groupPath, { label, fields: [] });
      groups.get(f.groupPath)!.fields.push(f);
    }
    return [...groups.values()];
  });

  protected field(): FieldDto | undefined {
    return this.fields().find(f => f.path === this.condition().field);
  }

  protected operators(): OperatorDto[] {
    return this.metadata.operatorsFor(this.field()?.dataType);
  }

  protected operator(): OperatorDto | undefined {
    return this.metadata.operators().find(o => o.operator === this.condition().operator);
  }

  protected opLabel(op: OperatorDto): string {
    return this.metadata.operatorLabel(op, this.field()?.dataType);
  }

  /** Other fields of the same type, for field-to-field comparison. */
  protected comparableFieldGroups(): FieldGroup[] {
    const type = this.field()?.dataType;
    return this.fieldGroups()
      .map(g => ({ label: g.label, fields: g.fields.filter(f => f.dataType === type && f.path !== this.condition().field) }))
      .filter(g => g.fields.length > 0);
  }

  protected onFieldChange(path: string): void {
    const c = this.condition();
    c.field = path;
    const ops = this.operators();
    if (!ops.some(o => o.operator === c.operator)) c.operator = ops[0]?.operator ?? '';
    if (c.value?.kind === 'Field') delete c.value;
    resetOperands(c, this.field(), this.operator());
    this.changed.emit();
  }

  protected onOperatorChange(op: string): void {
    this.condition().operator = op;
    resetOperands(this.condition(), this.field(), this.operator());
    this.changed.emit();
  }

  protected compareMode(): 'value' | 'field' {
    return this.condition().value?.kind === 'Field' ? 'field' : 'value';
  }

  protected setCompareMode(mode: 'value' | 'field'): void {
    this.condition().value = mode === 'field'
      ? { kind: 'Field', field: '' }
      : { kind: 'Constant', value: this.field()?.dataType === 'Boolean' ? false : null };
    this.changed.emit();
  }

  protected setCompareField(path: string): void {
    this.condition().value = { kind: 'Field', field: path };
    this.changed.emit();
  }

  /** Converts raw input text to the typed JSON value the engine expects. */
  protected setValue(which: 'value' | 'valueTo', raw: string | boolean | null): void {
    const type = this.field()?.dataType;
    let value: Scalar = raw;
    if (type === 'Number') value = raw === '' || raw === null ? null : Number(raw);
    if (type === 'Date' || type === 'String') value = raw === '' ? null : (raw as string);
    this.condition()[which] = { kind: 'Constant', value };
    this.changed.emit();
  }

  protected rawValue(which: 'value' | 'valueTo'): Scalar {
    return this.condition()[which]?.value ?? '';
  }

  /** "In" list editor: comma separated text -> typed list. */
  protected listText(): string {
    return (this.condition().value?.values ?? []).join(', ');
  }

  protected setList(text: string): void {
    const type = this.field()?.dataType;
    const values = text.split(',').map(v => v.trim()).filter(v => v !== '')
      .map(v => (type === 'Number' && !isNaN(Number(v)) ? Number(v) : v));
    this.condition().value = { kind: 'List', values };
    this.changed.emit();
  }

  protected inputType(): string {
    switch (this.field()?.dataType) {
      case 'Number': return 'number';
      case 'Date': return 'date';
      default: return 'text';
    }
  }
}
