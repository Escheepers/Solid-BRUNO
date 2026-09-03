import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { TopAppBar } from './shared/top-app-bar/top-app-bar';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, TopAppBar],
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App {}
