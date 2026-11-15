import { Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { emptyCondition, emptyGroup, nextId } from '../../core/definition.utils';
import { Condition, ConditionGroup, ConditionNode, FieldDto, LogicalOperator } from '../../core/models';
import { ConditionEditor } from './condition-editor';

/**
 * A group of conditions combined with AND / OR. Recursive: a group can contain nested groups,
 * which is how mixed logic like "A and (B or C)" is built without any SQL knowledge.
 */
@Component({
  selector: 'app-condition-group-editor',
  imports: [ConditionEditor, ConditionGroupEditor, MatButtonModule, MatButtonToggleModule, MatIconModule, MatTooltipModule],
  templateUrl: './condition-group-editor.html',
  styleUrl: './condition-group-editor.scss'
})
export class ConditionGroupEditor {
  readonly group = input.required<ConditionGroup>();
  /** Root of the whole tree (for unique id generation). */
  readonly root = input.required<ConditionGroup>();
  readonly fields = input.required<FieldDto[]>();
  readonly rootLabel = input('');
  readonly errors = input<Map<string, string[]>>(new Map());
  readonly depth = input(0);
  readonly changed = output<void>();
  readonly remove = output<void>();

  protected static readonly maxDepth = 4;

  protected asCondition(node: ConditionNode): Condition { return node as Condition; }
  protected asGroup(node: ConditionNode): ConditionGroup { return node as ConditionGroup; }

  protected setLogic(logic: LogicalOperator): void {
    this.group().logic = logic;
    this.changed.emit();
  }

  protected addCondition(): void {
    this.group().children.push(emptyCondition(nextId(this.root(), 'c')));
    this.changed.emit();
  }

  protected addGroup(): void {
    const id = nextId(this.root(), 'g');
    const child = emptyGroup(id, this.group().logic === 'And' ? 'Or' : 'And');
    this.group().children.push(child);
    child.children.push(emptyCondition(nextId(this.root(), 'c')));
    this.changed.emit();
  }

  protected removeChild(index: number): void {
    this.group().children.splice(index, 1);
    this.changed.emit();
  }

  protected canNest(): boolean {
    return this.depth() < ConditionGroupEditor.maxDepth;
  }

  protected logicLabel(): string {
    return this.group().logic === 'And' ? 'וגם' : 'או';
  }
}
