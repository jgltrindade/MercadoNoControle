import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { filter, switchMap } from 'rxjs';
import { toApiError } from '../../core/api/api-error';
import { NotificationService } from '../../core/services/notification.service';
import { ConfirmService } from '../../shared/components/confirm-dialog/confirm-dialog';
import { EmptyState } from '../../shared/components/empty-state/empty-state';
import { PageToolbar } from '../../shared/components/page-toolbar/page-toolbar';
import { CategoriesService, Category } from './categories.service';
import { CategoryFormDialog } from './category-form-dialog';

@Component({
  selector: 'app-categories-page',
  imports: [
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    MatProgressBarModule,
    MatTableModule,
    MatTooltipModule,
    EmptyState,
    PageToolbar,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './categories-page.html',
})
export class CategoriesPage {
  private readonly service = inject(CategoriesService);
  private readonly dialog = inject(MatDialog);
  private readonly confirmService = inject(ConfirmService);
  private readonly notification = inject(NotificationService);

  protected readonly columns = ['name', 'description', 'productCount', 'actions'];

  protected readonly categories = rxResource({ stream: () => this.service.list() });
  protected readonly rows = computed(() => (this.categories.hasValue() ? this.categories.value() : []));
  protected readonly loadError = computed(() => {
    const error = this.categories.error();
    return error ? toApiError(error).message : null;
  });

  openForm(category?: Category): void {
    this.dialog
      .open(CategoryFormDialog, { data: category ?? null, width: '480px' })
      .afterClosed()
      .pipe(filter(Boolean))
      .subscribe(() => this.categories.reload());
  }

  delete(category: Category): void {
    this.confirmService
      .confirm({
        title: 'Excluir categoria',
        message: `Deseja excluir a categoria "${category.name}"?`,
        confirmLabel: 'Excluir',
        destructive: true,
      })
      .pipe(
        filter(Boolean),
        switchMap(() => this.service.delete(category.id)),
      )
      .subscribe({
        next: () => {
          this.notification.success('Categoria excluída.');
          this.categories.reload();
        },
        error: (error: unknown) => this.notification.error(toApiError(error).message),
      });
  }
}
