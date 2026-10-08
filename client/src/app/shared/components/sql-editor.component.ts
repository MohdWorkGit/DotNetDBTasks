import {
  AfterViewInit, ChangeDetectorRef, Component, ElementRef, Input, NgZone, OnChanges, OnDestroy,
  SimpleChanges, ViewChild, inject
} from '@angular/core';
import { ControlValueAccessor, NgControl } from '@angular/forms';
import { Compartment, EditorState, Extension, RangeSet, RangeSetBuilder, StateField } from '@codemirror/state';
import {
  Decoration, DecorationSet, EditorView, GutterMarker, MatchDecorator, ViewPlugin, ViewUpdate,
  drawSelection, gutterLineClass, highlightActiveLine, highlightActiveLineGutter, keymap, lineNumbers,
  placeholder
} from '@codemirror/view';
import { defaultKeymap, history, historyKeymap, indentWithTab } from '@codemirror/commands';
import { bracketMatching, indentOnInput, syntaxHighlighting } from '@codemirror/language';
import {
  autocompletion, closeBrackets, closeBracketsKeymap, completionKeymap
} from '@codemirror/autocomplete';
import { PLSQL, sql } from '@codemirror/lang-sql';
import { classHighlighter } from '@lezer/highlight';

/**
 * @name is how a query's parameters are bound, and the lexer has no notion of it (to the PL/SQL
 * grammar "@" is just an operator). A decoration marks each one so the parameters a query
 * expects stand out from the column names around them.
 */
const paramMatcher = new MatchDecorator({
  regexp: /@[A-Za-z_][A-Za-z0-9_]*/g,
  decoration: Decoration.mark({ class: 'cm-sql-param' })
});

const paramHighlighter = ViewPlugin.fromClass(class {
  decorations: DecorationSet;
  constructor(view: EditorView) { this.decorations = paramMatcher.createDeco(view); }
  update(update: ViewUpdate) { this.decorations = paramMatcher.updateDeco(update, this.decorations); }
}, { decorations: plugin => plugin.decorations });

/**
 * Lines holding text that blocks saving (the host passes the pattern, e.g. a ";" or a "--"
 * comment) are tinted, the offending characters get a wavy underline, and the line number turns
 * red — so the message under the box and the place to fix it are visibly the same thing.
 *
 * <p>A StateField rather than a ViewPlugin because the gutter's line class is a state facet; it
 * scans the whole document, which is cheap at the sizes a saved query is limited to.</p>
 */
const problemLine = Decoration.line({ class: 'cm-problem-line' });
const problemMark = Decoration.mark({ class: 'cm-problem-mark' });
const problemGutter = new class extends GutterMarker { override elementClass = 'cm-problem-gutter'; }();

interface ProblemHighlights { decorations: DecorationSet; gutter: RangeSet<GutterMarker>; first: number | null; }

function problemHighlighting(pattern: RegExp): Extension {
  // Always scan globally, whatever flags the host's pattern was written with.
  const global = new RegExp(pattern.source, pattern.flags.includes('g') ? pattern.flags : pattern.flags + 'g');

  const compute = (state: EditorState): ProblemHighlights => {
    const text = state.doc.toString();
    const decorations = new RangeSetBuilder<Decoration>();
    const gutter = new RangeSetBuilder<GutterMarker>();
    let first: number | null = null;
    let lastLine = -1;
    global.lastIndex = 0;
    for (let m = global.exec(text); m; m = global.exec(text)) {
      if (m[0].length === 0) { global.lastIndex++; continue; }
      const line = state.doc.lineAt(m.index);
      // A line decoration sits at the line's start, so it is added before the marks inside it —
      // RangeSetBuilder needs ranges in document order.
      if (line.number !== lastLine) {
        decorations.add(line.from, line.from, problemLine);
        gutter.add(line.from, line.from, problemGutter);
        lastLine = line.number;
      }
      decorations.add(m.index, m.index + m[0].length, problemMark);
      first ??= m.index;
    }
    return { decorations: decorations.finish(), gutter: gutter.finish(), first };
  };

  const field = StateField.define<ProblemHighlights>({
    create: compute,
    update: (value, tr) => (tr.docChanged ? compute(tr.state) : value),
    provide: f => [
      EditorView.decorations.from(f, v => v.decorations),
      gutterLineClass.from(f, v => v.gutter)
    ]
  });
  return field;
}

/**
 * Colours come from the --sql-* tokens in styles.scss, so the editor follows the light/dark theme
 * without being rebuilt when the theme flips. The PL/SQL dialect is Oracle's, which is what every
 * connection in this application talks to.
 *
 * <p>Tokens get the fixed tok-* classes from classHighlighter and are coloured by the editor theme
 * below, not by a HighlightStyle. A HighlightStyle's rule is one generated class, and the global
 * dark-theme rule `body.dark-theme span { color: inherit }` outranks that, so every token went
 * plain white in dark mode. The theme's scoped `.cm-editor .tok-x` rules outrank it.</p>
 */
const editorTheme = EditorView.theme({
  '&': {
    fontSize: '13px',
    color: 'var(--text-primary)',
    backgroundColor: 'transparent'
  },
  '&.cm-focused': { outline: 'none' },
  '.cm-scroller': {
    fontFamily: "'Cascadia Mono', Consolas, 'Courier New', monospace",
    lineHeight: '1.6'
  },
  '.cm-content': { caretColor: 'var(--text-primary)', padding: '8px 0' },
  '.cm-cursor, .cm-dropCursor': { borderLeftColor: 'var(--text-primary)' },
  '.cm-gutters': {
    backgroundColor: 'transparent',
    color: 'var(--text-hint)',
    borderRight: '1px solid var(--border-color)'
  },
  '.cm-activeLine, .cm-activeLineGutter': { backgroundColor: 'var(--sql-active-line)' },
  '&.cm-focused > .cm-scroller > .cm-selectionLayer .cm-selectionBackground, .cm-selectionBackground':
    { backgroundColor: 'var(--sql-selection)' },
  '.cm-matchingBracket': {
    backgroundColor: 'var(--sql-selection)',
    outline: '1px solid var(--sql-keyword)'
  },
  '.cm-placeholder': { color: 'var(--text-hint)' },
  '.tok-keyword': { color: 'var(--sql-keyword)', fontWeight: '600' },
  '.tok-typeName, .tok-variableName2': { color: 'var(--sql-type)' },
  '.tok-string, .tok-string2': { color: 'var(--sql-string)' },
  '.tok-number, .tok-bool, .tok-literal, .tok-atom': { color: 'var(--sql-number)' },
  '.tok-comment': { color: 'var(--sql-comment)', fontStyle: 'italic' },
  '.tok-operator, .tok-punctuation': { color: 'var(--text-secondary)' },
  '.cm-sql-param, .cm-sql-param *': { color: 'var(--sql-param)', fontWeight: '600' },
  '.cm-problem-line': { backgroundColor: 'var(--sql-problem-line)' },
  '.cm-problem-mark': {
    textDecoration: 'underline wavy var(--status-error)',
    textUnderlineOffset: '3px',
    backgroundColor: 'var(--sql-problem-mark)'
  },
  '.cm-gutterElement.cm-problem-gutter': { color: 'var(--status-error)', fontWeight: '700' },
  // The suggestion list is a tooltip outside the editor box, so it needs the theme spelled out.
  '.cm-tooltip': {
    backgroundColor: 'var(--bg-card)',
    color: 'var(--text-primary)',
    border: '1px solid var(--border-color)',
    borderRadius: '6px'
  },
  '.cm-tooltip-autocomplete > ul > li[aria-selected]': {
    backgroundColor: 'var(--accent-primary)',
    color: 'var(--bg-card)'
  },
  '.cm-completionDetail': { color: 'var(--text-hint)' }
});

/**
 * A SQL editor that behaves as a form control: syntax colouring, line numbers, bracket matching
 * and keyword suggestions, bound with formControlName like the textarea it replaces — so the
 * required and length validators, and anything reading the control's value, are unchanged.
 *
 * <p>Not a MatFormFieldControl: the outlined field's notched label cannot sit over a multi-line
 * editor cleanly, so the host page supplies the label, hint and errors around it. The box styles
 * its own focus and error border from the bound control.</p>
 *
 * <p>Always left to right, also in Arabic: SQL is code.</p>
 */
@Component({
  standalone: true,
  selector: 'app-sql-editor',
  host: { '[class.fill-host]': 'fill' },
  template: `<div #host class="sql-editor" [class.focused]="focused" [class.invalid]="showError"
                  [class.disabled]="disabled" [class.fill]="fill" dir="ltr"></div>`,
  styles: [`
    :host { display: block; }
    .sql-editor {
      border: 1px solid var(--divider-color);
      border-radius: 4px;
      min-height: 140px;
      max-height: 480px;
      overflow: auto;
      text-align: left;
    }
    .sql-editor:hover { border-color: var(--text-secondary); }
    .sql-editor.focused { border-color: var(--accent-primary); box-shadow: inset 0 0 0 1px var(--accent-primary); }
    .sql-editor.invalid { border-color: var(--status-error); box-shadow: inset 0 0 0 1px var(--status-error); }
    .sql-editor.disabled { opacity: 0.6; }
    /* Fill mode (the expanded popup): take the host's whole height and scroll inside. */
    :host(.fill-host) { height: 100%; }
    .sql-editor.fill { height: 100%; min-height: 0; max-height: none; overflow: hidden; }
    .sql-editor.fill ::ng-deep .cm-editor { height: 100%; }
    .sql-editor.fill ::ng-deep .cm-scroller { overflow: auto; }
    /* Lets a click anywhere in the empty box land in the editor, not just on the first line. */
    .sql-editor ::ng-deep .cm-editor { min-height: 138px; }
  `]
})
export class SqlEditorComponent implements ControlValueAccessor, AfterViewInit, OnChanges, OnDestroy {
  @ViewChild('host', { static: true }) host!: ElementRef<HTMLDivElement>;

  @Input() placeholder = '';
  @Input() ariaLabel = '';
  /** Grow to the host's full height instead of the 140–480px box (used by the expanded popup). */
  @Input() fill = false;
  /** Focus the editor once it exists (the popup opens straight into typing). */
  @Input() autoFocus = false;
  /** Text that blocks saving; every match is highlighted along with its line. */
  @Input() problemPattern: RegExp | null = null;

  focused = false;
  disabled = false;

  private view?: EditorView;
  private pendingValue = '';
  /** True while writeValue is loading text, so the load is not reported back as an edit. */
  private writing = false;
  private readonly editable = new Compartment();
  private readonly problems = new Compartment();
  private onChange: (value: string) => void = () => {};
  private onTouched: () => void = () => {};

  // Registering through NgControl rather than NG_VALUE_ACCESSOR gives the component the bound
  // control, which is how it knows to draw the error border.
  readonly ngControl = inject(NgControl, { self: true, optional: true });
  private readonly zone = inject(NgZone);
  private readonly cdr = inject(ChangeDetectorRef);

  constructor() {
    if (this.ngControl) this.ngControl.valueAccessor = this;
  }

  /** Same rule as a Material field: show the error once the user has left the box. */
  get showError(): boolean {
    return !!this.ngControl?.invalid && !!this.ngControl?.touched;
  }

  ngAfterViewInit(): void {
    const state = EditorState.create({
      doc: this.pendingValue,
      extensions: [
        lineNumbers(),
        highlightActiveLineGutter(),
        highlightActiveLine(),
        history(),
        drawSelection(),
        indentOnInput(),
        bracketMatching(),
        closeBrackets(),
        autocompletion({ icons: false }),
        sql({ dialect: PLSQL, upperCaseKeywords: true }),
        syntaxHighlighting(classHighlighter),
        paramHighlighter,
        editorTheme,
        EditorView.lineWrapping,
        placeholder(this.placeholder),
        EditorView.contentAttributes.of({ 'aria-label': this.ariaLabel || 'SQL', 'aria-multiline': 'true' }),
        this.editable.of(this.editableExtensions()),
        this.problems.of(this.problemExtensions()),
        keymap.of([
          ...closeBracketsKeymap, ...completionKeymap, ...historyKeymap, ...defaultKeymap, indentWithTab
        ]),
        // CodeMirror listens to the DOM itself, not through Angular's event bindings, so nothing
        // tells Angular a keystroke happened. The app runs zoneless (NoopNgZone), so a form value
        // changed here would never reach the page — the parent's counter and errors would freeze,
        // and dev mode reports NG0100. markForCheck marks this view and its ancestors dirty and
        // schedules the update; zone.run covers the app being switched back to zone.js.
        EditorView.updateListener.of(update => {
          if (update.docChanged && !this.writing) {
            const value = update.state.doc.toString();
            this.zone.run(() => this.onChange(value));
            this.cdr.markForCheck();
          }
          if (update.focusChanged) {
            this.zone.run(() => {
              this.focused = update.view.hasFocus;
              if (!this.focused) this.onTouched();
            });
            this.cdr.markForCheck();
          }
        })
      ]
    });
    this.view = new EditorView({ state, parent: this.host.nativeElement });
    if (this.autoFocus) this.view.focus();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['problemPattern'] && this.view) {
      this.view.dispatch({ effects: this.problems.reconfigure(this.problemExtensions()) });
    }
  }

  ngOnDestroy(): void {
    this.view?.destroy();
  }

  /**
   * Scrolls to the n-th highlighted problem (0-based), selects it and puts the cursor there, so
   * "Show" beside a message lands exactly on the text to change. False when there is no such one.
   */
  revealProblem(n = 0): boolean {
    if (!this.view || !this.problemPattern) return false;
    const text = this.view.state.doc.toString();
    const flags = this.problemPattern.flags.includes('g') ? this.problemPattern.flags : this.problemPattern.flags + 'g';
    const match = [...text.matchAll(new RegExp(this.problemPattern.source, flags))].filter(m => m[0].length)[n];
    if (!match || match.index === undefined) return false;
    this.view.dispatch({
      selection: { anchor: match.index, head: match.index + match[0].length },
      effects: EditorView.scrollIntoView(match.index, { y: 'center' })
    });
    this.host.nativeElement.scrollIntoView({ behavior: 'smooth', block: 'center' });
    this.view.focus();
    return true;
  }

  writeValue(value: string | null): void {
    const text = value ?? '';
    if (!this.view) {
      this.pendingValue = text;
      return;
    }
    const current = this.view.state.doc.toString();
    if (current !== text) {
      // A programmatic load (e.g. the query arriving on the edit page) — replace the document
      // without it reading as a user edit, and without reporting it back to the form.
      this.writing = true;
      try {
        this.view.dispatch({ changes: { from: 0, to: current.length, insert: text } });
      } finally {
        this.writing = false;
      }
    }
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled = isDisabled;
    this.view?.dispatch({ effects: this.editable.reconfigure(this.editableExtensions()) });
    this.cdr.markForCheck();
  }

  private problemExtensions(): Extension {
    return this.problemPattern ? problemHighlighting(this.problemPattern) : [];
  }

  private editableExtensions() {
    return [EditorView.editable.of(!this.disabled), EditorState.readOnly.of(this.disabled)];
  }
}
