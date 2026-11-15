import { Condition, ConditionGroup, ConditionNode, FieldDto, Operand, OperandKind, OperatorDto, ValidationDefinition } from './models';

/** Helpers for editing the definition tree in the builder. */

export function emptyCondition(id: string): Condition {
  return { type: 'condition', id, field: '', operator: '' };
}

export function emptyGroup(id: string, logic: 'And' | 'Or' = 'And', withCondition?: string): ConditionGroup {
  return { type: 'group', id, logic, children: withCondition ? [emptyCondition(withCondition)] : [] };
}

export function newDefinition(rootEntity = ''): ValidationDefinition {
  return { rootEntity, condition: emptyGroup('root', 'And', 'c1'), displayFields: [] };
}

/** Next free id with the given prefix ("c" / "g") anywhere in the tree. */
export function nextId(root: ConditionGroup, prefix: 'c' | 'g'): string {
  let max = 0;
  const visit = (node: ConditionNode) => {
    const m = new RegExp(`^${prefix}(\\d+)$`).exec(node.id);
    if (m) max = Math.max(max, Number(m[1]));
    if (node.type === 'group') node.children.forEach(visit);
  };
  visit(root);
  return `${prefix}${max + 1}`;
}

export function countConditions(node: ConditionNode): number {
  return node.type === 'condition' ? 1 : node.children.reduce((sum, c) => sum + countConditions(c), 0);
}

/**
 * Normalizes a definition coming from the server: older/seeded JSON may use lowercase operand kinds
 * ("constant") while the API serializes PascalCase ("Constant"). The UI works with PascalCase.
 */
export function normalizeDefinition(definition: ValidationDefinition): ValidationDefinition {
  const copy: ValidationDefinition = JSON.parse(JSON.stringify(definition));
  const fixOperand = (o?: Operand) => {
    if (o?.kind) o.kind = (o.kind.charAt(0).toUpperCase() + o.kind.slice(1)) as OperandKind;
  };
  const visit = (node: ConditionNode) => {
    if (node.type === 'group') {
      node.logic = (node.logic.charAt(0).toUpperCase() + node.logic.slice(1).toLowerCase()) as 'And' | 'Or';
      node.children.forEach(visit);
    } else {
      fixOperand(node.value);
      fixOperand(node.valueTo);
    }
  };
  visit(copy.condition);
  copy.displayFields ??= [];
  return copy;
}

/** Resets the operands of a condition so they match the selected field/operator. */
export function resetOperands(condition: Condition, field: FieldDto | undefined, op: OperatorDto | undefined): void {
  if (!field || !op) { delete condition.value; delete condition.valueTo; return; }
  const defaultValue = field.dataType === 'Boolean' ? false : null;
  switch (op.shape) {
    case 'None':
      delete condition.value; delete condition.valueTo; break;
    case 'Single':
      if (condition.value?.kind !== 'Field' || !op.allowsFieldOperand) condition.value = { kind: 'Constant', value: defaultValue };
      delete condition.valueTo; break;
    case 'Range':
      condition.value = { kind: 'Constant', value: null };
      condition.valueTo = { kind: 'Constant', value: null }; break;
    case 'List':
      condition.value = { kind: 'List', values: [] };
      delete condition.valueTo; break;
  }
}

/**
 * Prepares the definition for the API: removes empty optional properties.
 * An operator not chosen yet ('') is omitted, so the backend validator reports it on that row
 * ("יש לבחור תנאי") - the strict JSON parser rightly rejects '' as an operator name.
 */
export function cleanDefinition(definition: ValidationDefinition): ValidationDefinition {
  const copy: ValidationDefinition = JSON.parse(JSON.stringify(definition));
  if (!copy.displayFields?.length) delete copy.displayFields;
  const visit = (node: ConditionNode) => {
    if (node.type === 'group') node.children.forEach(visit);
    else if (!node.operator) delete (node as Partial<Condition>).operator;
  };
  visit(copy.condition);
  return copy;
}
