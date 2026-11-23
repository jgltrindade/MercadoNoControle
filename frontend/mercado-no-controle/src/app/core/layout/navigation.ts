export interface NavigationItem {
  label: string;
  icon: string;
  path: string;
  /** Destaca o item somente quando a URL for exatamente `path`. */
  exact?: boolean;
}

/** Menu principal (especificação 9.2). */
export const NAVIGATION_ITEMS: readonly NavigationItem[] = [
  { label: 'Dashboard', icon: 'dashboard', path: '/dashboard' },
  { label: 'Notas fiscais', icon: 'receipt_long', path: '/receipts', exact: true },
  { label: 'Nova compra', icon: 'add_shopping_cart', path: '/receipts/new' },
  { label: 'Produtos', icon: 'inventory_2', path: '/products' },
  { label: 'Histórico de preços', icon: 'monitoring', path: '/price-history' },
  { label: 'Supermercados', icon: 'storefront', path: '/supermarkets' },
  { label: 'Categorias', icon: 'category', path: '/categories' },
  { label: 'Relatórios', icon: 'analytics', path: '/reports' },
  { label: 'Configurações', icon: 'settings', path: '/settings' },
];
